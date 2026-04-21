using AssistStudio.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Settings;

/// <summary>
/// Presentation model for a single scheduled task rendered by <see cref="Controls.ScheduleCard"/>.
/// </summary>
public sealed class ScheduleItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isDeleted;

    public ScheduleItemViewModel(ScheduleItem item)
    {
        Id = item.Id;
        Name = item.Name;
        Description = item.Description;
        Schedule = item.Schedule;
        ScheduleOnce = item.ScheduleOnce;
        OutputChannel = item.OutputChannel;
        CreatedAt = item.CreatedAt;
        _isEnabled = item.IsEnabled;
    }

    public string Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public string? Schedule { get; }
    public DateTimeOffset? ScheduleOnce { get; }
    public string? OutputChannel { get; }
    public DateTimeOffset CreatedAt { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (_isDeleted == value)
                return;

            _isDeleted = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public void MarkDeleted()
    {
        IsDeleted = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
