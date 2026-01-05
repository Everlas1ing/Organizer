using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Organizer.Models;
using Syncfusion.Maui.Scheduler;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Organizer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly Dictionary<string, Brush> _categoryBrushes = new();
    private readonly Random _random = new();
    private readonly SolidColorBrush _overdueBrush = new SolidColorBrush(Colors.Red);
    private readonly CategoryItem _noCategoryItem = new CategoryItem { Name = "Без категорії", ColorHex = "#808080" }; // Спеціальний об'єкт
   
    [ObservableProperty]
    private ObservableCollection<CategoryItem> _categoriesForPicker = new();
    private IDispatcherTimer _reminderTimer;
    private HashSet<string> _remindersShownThisSession = new();

    public MainViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _ = LoadDataAsync();

        ChangeViewCommand = new RelayCommand<string>(ChangeView);
        ShowAddFormCommand = new RelayCommand(ShowAddForm);
        ShowCategoryManagerCommand = new RelayCommand(ShowCategoryManager);
        ShowMenuCommand = new RelayCommand(ShowMenu);
        SaveTaskCommand = new AsyncRelayCommand(SaveTask);
        ShowTaskDetailsCommand = new AsyncRelayCommand<SchedulerAppointment>(ShowTaskDetailsAsync); // Ця команда тепер обробляє і активні, і завершені
        GoToEditFromDetailsCommand = new AsyncRelayCommand(GoToEditFromDetailsAsync);
        DeleteTaskFromDetailsCommand = new AsyncRelayCommand(DeleteTaskFromDetailsAsync);
        AddNewCategoryCommand = new AsyncRelayCommand(AddNewCategory);
        DeleteCategoryCommand = new AsyncRelayCommand<CategoryItem>(DeleteCategory);
        ShowEditCategoryCommand = new RelayCommand<CategoryItem>(ShowEditCategory);
        SaveEditCategoryCommand = new AsyncRelayCommand(SaveEditCategory);
        ToggleTaskCompletionCommand = new AsyncRelayCommand(ToggleTaskCompletion);
        SortByCategoryCommand = new AsyncRelayCommand(SortByCategory);

        StartReminderTimer();
    }

    private void StartReminderTimer()
    {
        _reminderTimer = Application.Current.Dispatcher.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(30); // Перевіряємо частіше (30 сек), щоб не пропустити хвилину
        _reminderTimer.Tick += async (s, e) =>
        {
            try
            {
                // НЕ викликаємо LoadDataAsync тут, це занадто важко.
                // Беремо завдання безпосередньо з сервісу, не оновлюючи UI:
                var tasks = await _databaseService.GetTasksAsync();
                var activeTasks = tasks.Where(t => !t.IsCompleted).ToList();
                var tasksToNotify = new List<TodoItem>();

                foreach (var task in activeTasks)
                {
                    // Перевірка, чи ми вже показували нагадування
                    if (_remindersShownThisSession.Contains(task.EventId)) continue;

                    if (task.ReminderValue > 0)
                    {
                        TimeSpan timeBefore = task.ReminderUnit switch
                        {
                            "хвилин" => TimeSpan.FromMinutes(task.ReminderValue),
                            "годин" => TimeSpan.FromHours(task.ReminderValue),
                            "днів" => TimeSpan.FromDays(task.ReminderValue),
                            _ => TimeSpan.Zero
                        };

                        if (timeBefore == TimeSpan.Zero) continue;

                        DateTime notificationTime = task.StartTime.Subtract(timeBefore);

                        // Логіка: Час нагадування настав АБО вже минув (але ми ще не повідомляли), 
                        // і саме завдання ще не почалося (або почалося зовсім нещодавно)
                        if (DateTime.Now >= notificationTime && DateTime.Now < task.StartTime.AddMinutes(5))
                        {
                            tasksToNotify.Add(task);
                            // Додаємо в кеш "показаних" тільки перед реальним показом
                        }
                    }
                }

                // Прибрали перевірку SidebarView == "Menu". Нагадування має бути поверх усього.
                if (tasksToNotify.Any())
                {
                    foreach (var task in tasksToNotify)
                    {
                        // Додаємо сюди, щоб не спамити, якщо користувач довго не закриває Alert
                        _remindersShownThisSession.Add(task.EventId);

                        // Використовуємо Dispatcher для гарантії роботи в UI потоці
                        await Application.Current.Dispatcher.DispatchAsync(async () =>
                        {
                            await Application.Current.MainPage.DisplayAlert(
                                "Нагадування!",
                                $"Завдання '{task.TaskName}' починається о {task.StartTime:HH:mm}!",
                                "OK");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Помилка в таймері нагадувань: {ex.Message}");
            }
        };
        _reminderTimer.Start();
    }


    #region Властивості стану UI
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuVisible), nameof(IsAddEditVisible), nameof(IsCategoryVisible), nameof(IsDetailsVisible), nameof(IsEditCategoryVisible))]
    private string _sidebarView = "Menu";
    public bool IsMenuVisible => SidebarView == "Menu";
    public bool IsAddEditVisible => SidebarView == "AddEditTask";
    public bool IsCategoryVisible => SidebarView == "ManageCategories";
    public bool IsDetailsVisible => SidebarView == "ViewDetails";
    public bool IsEditCategoryVisible => SidebarView == "EditCategory";
    #endregion

    #region Властивості даних
    [ObservableProperty] private ObservableCollection<SchedulerAppointment> _events = new();
    [ObservableProperty] private ObservableCollection<CategoryItem> _userCategories = new();
    [ObservableProperty] private ObservableCollection<SchedulerAppointment> _completedEvents = new();
    #endregion

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortButtonText))]
    private string _currentSortOrder = "Time"; 
    public string SortButtonText => CurrentSortOrder == "Time" ? "Сорт. за категорією" : "Сорт. за часом";
    
    #region Властивості форми (Завдання)
    [ObservableProperty] private string _currentTaskName;
    [ObservableProperty] private string _currentTaskDescription;
    [ObservableProperty] private CategoryItem _selectedCategory;
    [ObservableProperty] private DateTime _currentTaskStartDate = DateTime.Now;
    [ObservableProperty] private TimeSpan _currentTaskStartTime = DateTime.Now.TimeOfDay;
    [ObservableProperty] private DateTime _currentTaskEndDate = DateTime.Now;
    [ObservableProperty] private TimeSpan _currentTaskEndTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle;
    private string _currentTaskEventId;
    [ObservableProperty] private int _currentTaskReminderValue;
    [ObservableProperty] private string _selectedReminderUnit;
    public List<string> ReminderUnits { get; } = new List<string> { "без нагадування", "хвилин", "годин", "днів" };
    #endregion

    #region Властивості форми (Категорії)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewCategoryColor))]
    private string _newCategoryColorHex;
    public Color NewCategoryColor
    {
        get { try { return Color.FromHex(NewCategoryColorHex); } catch { return Colors.Red; } }
        set { if (value != null) SetProperty(ref _newCategoryColorHex, value.ToHex(), nameof(NewCategoryColorHex)); }
    }
    [ObservableProperty] private string _newCategoryInput;
    private CategoryItem _categoryToEdit;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditCategoryColor))]
    private string _editCategoryColorHex;
    public Color EditCategoryColor
    {
        get { try { return Color.FromHex(EditCategoryColorHex); } catch { return Colors.Red; } }
        set { if (value != null) SetProperty(ref _editCategoryColorHex, value.ToHex(), nameof(EditCategoryColorHex)); }
    }
    [ObservableProperty] private string _editCategoryName;
    #endregion

    #region Властивості (Деталі)
    [ObservableProperty] private string _selectedTaskName;
    [ObservableProperty] private string _selectedTaskDescription;
    [ObservableProperty] private string _selectedTaskCategory;
    [ObservableProperty] private string _selectedTaskTimeRange;
    [ObservableProperty] private Brush _selectedTaskCategoryBrush;
    private SchedulerAppointment _currentlyViewedAppointment;
    [ObservableProperty] private string _selectedTaskStatusText;
    [ObservableProperty] private Color _selectedTaskStatusColor;
    [ObservableProperty] private string _toggleButtonText;
    [ObservableProperty] private bool _selectedTaskIsCompleted;
    [ObservableProperty] private string selectedTaskReminderInfo;

    #endregion

    #region Команди
    public ICommand ChangeViewCommand { get; }
    public ICommand ShowAddFormCommand { get; }
    public ICommand ShowCategoryManagerCommand { get; }
    public ICommand ShowMenuCommand { get; }
    public ICommand SaveTaskCommand { get; }
    public IAsyncRelayCommand<SchedulerAppointment> ShowTaskDetailsCommand { get; }
    public ICommand GoToEditFromDetailsCommand { get; }
    public ICommand DeleteTaskFromDetailsCommand { get; }
    public ICommand AddNewCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand ShowEditCategoryCommand { get; }
    public ICommand SaveEditCategoryCommand { get; }
    public IAsyncRelayCommand ToggleTaskCompletionCommand { get; }
    public ICommand SortByCategoryCommand { get; }
    #endregion

    #region Методи завантаження та перетворення
    private async Task LoadDataAsync(bool clearReminderCache = true)
    {
        if (clearReminderCache)
        {
            _remindersShownThisSession.Clear();
        }

        var tasksFromDb = await _databaseService.GetTasksAsync();
        var categoriesFromDb = await _databaseService.GetCategoriesAsync();

        IEnumerable<TodoItem> sortedTasks;
        if (CurrentSortOrder == "Category")
        {
            sortedTasks = tasksFromDb.OrderBy(t => t.Category).ThenBy(t => t.StartTime);
        }
        else 
        {
            sortedTasks = tasksFromDb.OrderBy(t => t.StartTime);
        }


        var activeEvents = new ObservableCollection<SchedulerAppointment>();
        var completedEventsList = new ObservableCollection<SchedulerAppointment>();

        _categoryBrushes.Clear();
        foreach (var cat in categoriesFromDb)
        {
            GetBrushForCategory(cat.Name, cat.ColorHex);
        }

        foreach (var task in sortedTasks)
        {
            var appointment = ConvertTaskToAppointment(task);

            if (task.IsCompleted)
            {
                completedEventsList.Add(appointment);
            }
            else
            {
                activeEvents.Add(appointment);
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Events = activeEvents;
            CompletedEvents = completedEventsList;
            UserCategories = new ObservableCollection<CategoryItem>(categoriesFromDb.OrderBy(c => c.Name));

            var pickerList = new List<CategoryItem> { _noCategoryItem };
            pickerList.AddRange(categoriesFromDb.OrderBy(c => c.Name));
            CategoriesForPicker = new ObservableCollection<CategoryItem>(pickerList);
        });
    }

    private SchedulerAppointment ConvertTaskToAppointment(TodoItem task)
    {
        bool isOverdue = !task.IsCompleted && task.EndTime < DateTime.Now;
        return new SchedulerAppointment
        {
            Subject = isOverdue ? $"[ПРОСТРОЧЕНО] {task.TaskName}" : task.TaskName,
            StartTime = task.StartTime,
            EndTime = task.EndTime,
            Background = isOverdue ? _overdueBrush : GetBrushForCategory(task.Category),
            Notes = task.EventId
        };
    }

    private Brush GetBrushForCategory(string categoryName, string colorHex = null)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return new SolidColorBrush(Colors.Gray);
        if (_categoryBrushes.TryGetValue(categoryName, out Brush brush)) return brush;
        Color color;
        try
        {
            if (!string.IsNullOrWhiteSpace(colorHex)) color = Color.FromHex(colorHex);
            else color = Color.FromRgb(_random.Next(100, 230), _random.Next(100, 230), _random.Next(100, 230));
        }
        catch { color = Colors.MediumPurple; }
        var newBrush = new SolidColorBrush(color);
        _categoryBrushes[categoryName] = newBrush;
        return newBrush;
    }
    #endregion

    #region Логіка керування формою (Завдання)
    private void ClearForm()
    {
        CurrentTaskName = string.Empty;
        CurrentTaskDescription = string.Empty;
        SelectedCategory = _noCategoryItem;
        CurrentTaskStartDate = DateTime.Now;
        CurrentTaskStartTime = DateTime.Now.TimeOfDay;
        CurrentTaskEndDate = DateTime.Now;
        CurrentTaskEndTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
        CurrentTaskReminderValue = 10;
        SelectedReminderUnit = "хвилин";
        _currentTaskEventId = null;
        IsEditing = false;
        FormTitle = "Нове завдання";
    }
    private void ShowMenu() => SidebarView = "Menu";
    private void ShowAddForm() { ClearForm(); SidebarView = "AddEditTask"; }
    public void ShowAddFormAt(DateTime time)
    {
        ClearForm();
        CurrentTaskStartDate = time.Date;
        CurrentTaskStartTime = time.TimeOfDay;
        CurrentTaskEndDate = time.Date;
        CurrentTaskEndTime = time.TimeOfDay.Add(TimeSpan.FromHours(1));
        SidebarView = "AddEditTask";
    }
    public async Task ShowEditFormAsync(SchedulerAppointment appointment)
    {
        var eventId = appointment.Notes;
        if (string.IsNullOrWhiteSpace(eventId)) return;
        var task = await _databaseService.GetTaskByEventIdAsync(eventId);
        if (task == null) return;
        CurrentTaskName = task.TaskName;
        CurrentTaskDescription = task.Description;
        if (string.IsNullOrEmpty(task.Category))
        {
            SelectedCategory = _noCategoryItem;
        }
        else
        {
            SelectedCategory = CategoriesForPicker.FirstOrDefault(c => c.Name == task.Category) ?? _noCategoryItem;
        }
        CurrentTaskStartDate = task.StartTime.Date;
        CurrentTaskStartTime = task.StartTime.TimeOfDay;
        CurrentTaskEndDate = task.EndTime.Date;
        CurrentTaskEndTime = task.EndTime.TimeOfDay;
        CurrentTaskReminderValue = task.ReminderValue > 0 ? task.ReminderValue : 10;
        SelectedReminderUnit = string.IsNullOrEmpty(task.ReminderUnit) || task.ReminderValue < 0 ? "без нагадування" : task.ReminderUnit;
        _currentTaskEventId = task.EventId;
        IsEditing = true;
        FormTitle = "Редагувати завдання";
        SidebarView = "AddEditTask";
    }

    private async Task SaveTask()
    {
        try
        {
            DateTime finalStart = CurrentTaskStartDate.Date + CurrentTaskStartTime;
            DateTime finalEnd = CurrentTaskEndDate.Date + CurrentTaskEndTime;
            if (finalEnd <= finalStart) { await Application.Current.MainPage.DisplayAlert("Помилка", "Час завершення має бути пізніше часу початку", "OK"); return; }

            TodoItem task;
            if (IsEditing) { task = await _databaseService.GetTaskByEventIdAsync(_currentTaskEventId); if (task == null) return; }
            else { task = new TodoItem { EventId = Guid.NewGuid().ToString() }; }

            task.TaskName = CurrentTaskName;
            task.Description = CurrentTaskDescription;
            if (SelectedCategory == null || SelectedCategory.Name == _noCategoryItem.Name)
            {
                task.Category = null; 
            }
            else
            {
                task.Category = SelectedCategory.Name;
            }
            task.StartTime = finalStart;
            task.EndTime = finalEnd;

            if (SelectedReminderUnit == "без нагадування") { task.ReminderValue = -1; task.ReminderUnit = null; }
            else { task.ReminderValue = CurrentTaskReminderValue; task.ReminderUnit = SelectedReminderUnit; }

            task.IsCompleted = false;
            await _databaseService.SaveTaskAsync(task);

            if (IsEditing) _remindersShownThisSession.Remove(task.EventId);

            await LoadDataAsync();
            ShowMenu();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save task: {ex.Message}");
            await Application.Current.MainPage.DisplayAlert("Помилка", "Не вдалося зберегти завдання", "OK");
        }
    }
    #endregion

    #region Логіка керування (Категорії)
    private string GetRandomHex() { return $"#{_random.Next(0, 256):X2}{_random.Next(0, 256):X2}{_random.Next(0, 256):X2}"; }
    private void ShowCategoryManager() { NewCategoryInput = string.Empty; NewCategoryColorHex = GetRandomHex(); SidebarView = "ManageCategories"; }
    private bool IsValidHex(string hex) { return Regex.IsMatch(hex, @"^#(?:[0-9a-fA-F]{3}){1,2}$"); }
    private async Task AddNewCategory()
    {
        var newName = NewCategoryInput?.Trim(); var newHex = NewCategoryColorHex?.Trim();
        if (string.IsNullOrWhiteSpace(newName)) { await Application.Current.MainPage.DisplayAlert("Помилка", "Назва категорії не може бути порожньою.", "OK"); return; }
        if (!IsValidHex(newHex)) { await Application.Current.MainPage.DisplayAlert("Помилка", "Неправильний формат кольору.", "OK"); return; }
        if (UserCategories.Any(c => c.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))) { await Application.Current.MainPage.DisplayAlert("Помилка", "Така категорія вже існує.", "OK"); return; }
        var newCategory = new CategoryItem { Name = newName, ColorHex = newHex };
        await _databaseService.SaveCategoryAsync(newCategory);
        GetBrushForCategory(newName, newHex); UserCategories.Add(newCategory);
        UserCategories = new ObservableCollection<CategoryItem>(UserCategories.OrderBy(c => c.Name));
        NewCategoryInput = string.Empty; NewCategoryColorHex = GetRandomHex();
    }
    private async Task DeleteCategory(CategoryItem category)
    {
        if (category == null) return;
        bool confirm = await Application.Current.MainPage.DisplayAlert(
            "Видалення",
            $"Видалити '{category.Name}'? Всі завдання цієї категорії стануть 'Без категорії'.",
            "Так", "Ні");
        if (confirm)
        {
            try
            {
                await _databaseService.UpdateTaskCategoryNameAsync(category.Name, null);
                await _databaseService.DeleteCategoryAsync(category);
                _categoryBrushes.Remove(category.Name);
                await LoadDataAsync();
                ShowCategoryManager();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Помилка видалення категорії: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert("Помилка", "Не вдалося видалити категорію.", "OK");
            }
        }
    }
    private void ShowEditCategory(CategoryItem category)
    {
        if (category == null) return;
        _categoryToEdit = category; EditCategoryName = category.Name; EditCategoryColorHex = category.ColorHex; SidebarView = "EditCategory";
    }
    private async Task SaveEditCategory()
    {
        var oldName = _categoryToEdit.Name; var newName = EditCategoryName?.Trim(); var newHex = EditCategoryColorHex?.Trim();
        if (string.IsNullOrWhiteSpace(newName)) { await Application.Current.MainPage.DisplayAlert("Помилка", "Назва не може бути порожньою.", "OK"); return; }
        if (!IsValidHex(newHex)) { await Application.Current.MainPage.DisplayAlert("Помилка", "Неправильний формат кольору.", "OK"); return; }
        if (UserCategories.Any(c => c.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && c.ID != _categoryToEdit.ID)) { await Application.Current.MainPage.DisplayAlert("Помилка", "Така назва категорії вже існує.", "OK"); return; }
        _categoryToEdit.Name = newName; _categoryToEdit.ColorHex = newHex;
        await _databaseService.UpdateCategoryAsync(_categoryToEdit);
        if (oldName != newName) { await _databaseService.UpdateTaskCategoryNameAsync(oldName, newName); }
        await LoadDataAsync(); SidebarView = "ManageCategories";
    }
    #endregion

    #region Логіка (Деталі)

    private async Task ShowTaskDetailsAsync(SchedulerAppointment appointment)
    {
        var eventId = appointment.Notes;
        if (string.IsNullOrWhiteSpace(eventId)) return;
        var task = await _databaseService.GetTaskByEventIdAsync(eventId);
        if (task == null) return;

        _currentlyViewedAppointment = appointment;
        SelectedTaskName = task.TaskName;
        SelectedTaskDescription = string.IsNullOrWhiteSpace(task.Description) ? "Опис відсутній" : task.Description;
        SelectedTaskCategory = string.IsNullOrWhiteSpace(task.Category) ? "Без категорії" : task.Category;
        SelectedTaskTimeRange = $"{task.StartTime:HH:mm} - {task.EndTime:HH:mm} ({task.StartTime:dd.MM.yyyy})";
        SelectedTaskIsCompleted = task.IsCompleted;

        if (task.ReminderValue > 0 && !string.IsNullOrEmpty(task.ReminderUnit))
        {
            SelectedTaskReminderInfo = $"Нагадування: за {task.ReminderValue} {task.ReminderUnit} до початку.";
        }
        else
        {
            SelectedTaskReminderInfo = "Нагадування: не встановлено.";
        }

        if (task.IsCompleted)
        {
            SelectedTaskStatusText = "Завдання завершене";
            SelectedTaskStatusColor = Colors.Green;
            SelectedTaskCategoryBrush = new SolidColorBrush(Colors.Gray);
            ToggleButtonText = "Активувати";
        }
        else if (task.EndTime < DateTime.Now)
        {
            SelectedTaskStatusText = "ЗАВДАННЯ ПРОСТРОЧЕНЕ";
            SelectedTaskStatusColor = Colors.Red;
            SelectedTaskCategoryBrush = _overdueBrush;
            ToggleButtonText = "Завершити";
        }
        else
        {
            SelectedTaskStatusText = "Активне завдання";
            SelectedTaskStatusColor = Colors.White;
            SelectedTaskCategoryBrush = GetBrushForCategory(task.Category);
            ToggleButtonText = "Завершити";
        }

        SidebarView = "ViewDetails";
    }

    private async Task GoToEditFromDetailsAsync()
    {
        if (_currentlyViewedAppointment == null) return;
        await ShowEditFormAsync(_currentlyViewedAppointment);
    }

    private async Task DeleteTaskFromDetailsAsync()
    {
        if (_currentlyViewedAppointment == null) return;
        bool confirm = await Application.Current.MainPage.DisplayAlert("Підтвердження", "Видалити цю подію?", "Так", "Ні");
        if (confirm)
        {
            await DeleteEventByAppointmentAsync(_currentlyViewedAppointment);
            _currentlyViewedAppointment = null;
            SidebarView = "Menu";
        }
    }

    public async Task DeleteEventByAppointmentAsync(SchedulerAppointment appointment)
    {
        if (appointment == null) return;
        var eventId = appointment.Notes;
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            var task = await _databaseService.GetTaskByEventIdAsync(eventId);
            if (task != null) await _databaseService.DeleteTaskAsync(task);
        }
        await LoadDataAsync();
    }

    private async Task ToggleTaskCompletion()
    {
        if (_currentlyViewedAppointment == null) return;
        var eventId = _currentlyViewedAppointment.Notes;
        var task = await _databaseService.GetTaskByEventIdAsync(eventId);
        if (task == null) return;

        task.IsCompleted = !task.IsCompleted;
        await _databaseService.SaveTaskAsync(task);

        if (task.IsCompleted) _remindersShownThisSession.Remove(task.EventId);

        await LoadDataAsync();
        SidebarView = "Menu";
    }

    private void ChangeView(string viewType)
    {
        Console.WriteLine($"Changing view to: {viewType}");
    }
    private async Task SortByCategory()
    {
        if (CurrentSortOrder == "Time")
        {
            CurrentSortOrder = "Category";
        }
        else
        {
            CurrentSortOrder = "Time";
        }

        await LoadDataAsync();
    }
    #endregion
}