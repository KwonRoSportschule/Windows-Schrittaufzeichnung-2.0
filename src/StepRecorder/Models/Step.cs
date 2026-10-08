using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace StepRecorder.Models;

public enum StepKind { Click, DoubleClick, RightClick, MiddleClick, Keyboard, Shortcut, Comment }

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Step : Observable
{
    private int _number;
    private string _description = "";
    private string _note = "";
    private StepKind _kind;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public StepKind Kind { get => _kind; set { if (Set(ref _kind, value)) OnPropertyChanged(nameof(KindGlyph)); } }
    public string Description { get => _description; set => Set(ref _description, value ?? ""); }
    public string Note { get => _note; set { if (Set(ref _note, value ?? "")) OnPropertyChanged(nameof(HasNote)); } }

    public string WindowTitle { get; set; } = "";
    public string ProcessName { get; set; } = "";

    /// <summary>File names relative to the session directory.</summary>
    public string ImageFile { get; set; } = "";
    public string ThumbFile { get; set; } = "";

    [JsonIgnore] public int Number { get => _number; set => Set(ref _number, value); }
    [JsonIgnore] public string ImagePath { get; set; } = "";
    [JsonIgnore] public string ThumbPath { get; set; } = "";
    [JsonIgnore] public bool HasNote => !string.IsNullOrWhiteSpace(_note);

    /// <summary>Segoe Fluent Icons glyph for the list.</summary>
    [JsonIgnore]
    public string KindGlyph => Kind switch
    {
        StepKind.Keyboard or StepKind.Shortcut => "",
        StepKind.Comment => "",
        _ => ""
    };
}

public sealed class Session : Observable
{
    private string _title = "";
    private bool _isDirty;

    public Session(string directory)
    {
        Directory = directory;
        Steps.CollectionChanged += OnStepsChanged;
    }

    public string Directory { get; }
    public DateTime Created { get; set; } = DateTime.Now;
    public ObservableCollection<Step> Steps { get; } = new();
    public List<string> Videos { get; } = new();
    public string? FilePath { get; set; }

    public string Title { get => _title; set { if (Set(ref _title, value ?? "")) IsDirty = true; } }
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Step s in e.NewItems) { s.PropertyChanged -= OnStepEdited; s.PropertyChanged += OnStepEdited; }
        if (e.OldItems != null && e.Action == NotifyCollectionChangedAction.Remove)
            foreach (Step s in e.OldItems) s.PropertyChanged -= OnStepEdited;
        Renumber();
        IsDirty = true;
    }

    private void OnStepEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Step.Description) or nameof(Step.Note) or nameof(Step.Kind)) IsDirty = true;
    }

    public void Renumber()
    {
        for (int i = 0; i < Steps.Count; i++) Steps[i].Number = i + 1;
    }
}
