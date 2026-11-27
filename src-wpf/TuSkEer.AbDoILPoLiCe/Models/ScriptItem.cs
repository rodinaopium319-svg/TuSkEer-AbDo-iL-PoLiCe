using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TuSkEer.AbDoILPoLiCe.Models;

public sealed class ScriptItem : INotifyPropertyChanged
{
    private long _id;
    private string _name = string.Empty;
    private string _content = string.Empty;
    private HotkeyBinding _hotkey = new();
    private DateTime _createdAtUtc = DateTime.UtcNow;
    private DateTime _modifiedAtUtc = DateTime.UtcNow;
    private bool _isEnabled = true;
    private string _category = "General";
    private int _sortOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Content
    {
        get => _content;
        set
        {
            if (SetField(ref _content, value))
            {
                OnPropertyChanged(nameof(SentenceCount));
                OnPropertyChanged(nameof(CharacterCount));
                OnPropertyChanged(nameof(PreviewLine));
            }
        }
    }

    public HotkeyBinding Hotkey
    {
        get => _hotkey;
        set
        {
            if (SetField(ref _hotkey, value ?? new HotkeyBinding()))
            {
                OnPropertyChanged(nameof(HotkeyDisplay));
                OnPropertyChanged(nameof(HasHotkey));
            }
        }
    }

    public DateTime CreatedAtUtc
    {
        get => _createdAtUtc;
        set => SetField(ref _createdAtUtc, value);
    }

    public DateTime ModifiedAtUtc
    {
        get => _modifiedAtUtc;
        set => SetField(ref _modifiedAtUtc, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public string Category
    {
        get => _category;
        set => SetField(ref _category, value);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetField(ref _sortOrder, value);
    }

    [JsonIgnore]
    public string HotkeyDisplay => Hotkey.IsEmpty ? "Unassigned" : Hotkey.DisplayString;

    [JsonIgnore]
    public bool HasHotkey => !Hotkey.IsEmpty;

    [JsonIgnore]
    public int SentenceCount => SplitSentences(Content).Length;

    [JsonIgnore]
    public int CharacterCount => Content?.Length ?? 0;

    [JsonIgnore]
    public string PreviewLine
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content)) return "Empty script";
            var lines = SplitSentences(Content);
            return lines.Length > 0 ? lines[0] : "Empty script";
        }
    }

    public void NotifyHotkeyChanged()
    {
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(HasHotkey));
    }

    public static string[] SplitSentences(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();

        var rawLines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var list = new List<string>(rawLines.Length);
        foreach (var line in rawLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                list.Add(line);
            }
        }
        return list.ToArray();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
