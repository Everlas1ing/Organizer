Project Overview
This project is a robust, feature-rich Task Organizer and Scheduling Application built using .NET MAUI. It leverages the Model-View-ViewModel (MVVM) architectural pattern via the CommunityToolkit.Mvvm library to ensure clean separation of logic and UI. For calendar and event visualization, it integrates the Syncfusion MAUI Scheduler control.

The application allows users to create tasks, assign them to custom color-coded categories, set dynamic reminders, and track their completion status directly within a unified calendar interface.

Key Features
Comprehensive Task Management: Create, edit, delete, and view detailed information for scheduled tasks.

Custom Color-Coded Categories: Users can create custom categories with specific HEX colors, making it easy to visually distinguish different types of tasks on the calendar.

Smart Reminder System: A built-in background timer checks for upcoming tasks and triggers local alerts based on user-defined intervals (minutes, hours, or days before the event).

Status Tracking: Tasks are automatically evaluated and visually flagged based on their state: Active (custom color), Completed (grayed out), or Overdue (highlighted in red).

Dynamic Sorting: Instantly toggle the calendar's underlying task list to sort by chronologically ascending time or by category groupings.

Single-Page UI State Routing: Smooth transitions between menus, forms, and details using a reactive sidebar state manager.

How It Works: Technical Architecture
UI State Management
The app uses a single-page approach governed by the SidebarView property. Instead of navigating between multiple different pages, the ViewModel triggers UI changes by updating this string property (e.g., "Menu", "AddEditTask", "ManageCategories", "ViewDetails"). Reactive boolean properties (IsMenuVisible, IsAddEditVisible, etc.) automatically show or hide the relevant UI components.

Data Mapping & Syncfusion Integration
The core data models (TodoItem) are stored via a local SQLite database (DatabaseService). Before rendering the calendar, the ViewModel maps these database records into SchedulerAppointment objects required by Syncfusion.
During this conversion, the application dynamically calculates the visual appearance of the appointment based on its deadline and completion status.

The Reminder Engine
The application employs an asynchronous polling mechanism to handle user notifications:

An IDispatcherTimer is initialized at startup and ticks every 30 seconds.

It fetches uncompleted tasks and calculates the exact notification threshold by subtracting the user's chosen reminder value from the task's start time.

If the current time falls within the notification window, the app queues the task and dispatches an alert to the main UI thread.

A localized session cache (_remindersShownThisSession) ensures users are not repeatedly spammed with the same notification.

Data Models & Collections
The ViewModel maintains several reactive collections (ObservableCollection) to keep the UI perfectly synchronized with the data state:
