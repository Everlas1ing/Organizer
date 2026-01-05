using Organizer.Models;
using SQLite;

public class DatabaseService
{
    private SQLiteAsyncConnection _database;
    private string _dbPath = Path.Combine(FileSystem.AppDataDirectory, "organizer.db3");

    public DatabaseService()
    {
        _database = new SQLiteAsyncConnection(_dbPath);
        _database.CreateTableAsync<TodoItem>().Wait();
        _database.CreateTableAsync<CategoryItem>().Wait();
    }

    public Task<List<TodoItem>> GetTasksAsync() =>
        _database.Table<TodoItem>().ToListAsync();

    public Task<TodoItem?> GetTaskByEventIdAsync(string eventId) =>
        _database.Table<TodoItem>().Where(t => t.EventId == eventId).FirstOrDefaultAsync();

    public Task<int> SaveTaskAsync(TodoItem item)
    {
        if (item.ID != 0)
            return _database.UpdateAsync(item);
        else
            return _database.InsertAsync(item);
    }

    public Task<int> DeleteTaskAsync(TodoItem item) =>
        _database.DeleteAsync(item);


    public Task<List<CategoryItem>> GetCategoriesAsync() =>
        _database.Table<CategoryItem>().ToListAsync();

    public Task<CategoryItem> GetCategoryByNameAsync(string name) =>
        _database.Table<CategoryItem>().Where(c => c.Name == name).FirstOrDefaultAsync();

    public async Task<int> SaveCategoryAsync(CategoryItem item)
    {
        try
        {
            return await _database.InsertAsync(item);
        }
        catch (SQLiteException)
        {
            return 0; 
        }
    }

    public Task<int> DeleteCategoryAsync(CategoryItem item) =>
        _database.DeleteAsync(item);

    public Task<int> UpdateCategoryAsync(CategoryItem item) =>
        _database.UpdateAsync(item);

    public async Task<int> UpdateTaskCategoryNameAsync(string oldName, string newName)
    {
        return await _database.ExecuteAsync(
            "UPDATE TodoItem SET Category = ? WHERE Category = ?",
            newName,
            oldName);
    }

}