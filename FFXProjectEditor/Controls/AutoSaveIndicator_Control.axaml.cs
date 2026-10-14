using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace FFXProjectEditor;

public partial class AutoSaveIndicator_Control : UserControl, IDisposable
{
    public static readonly StyledProperty<ByteSnapshotEditorSession?> SessionProperty =
        AvaloniaProperty.Register<AutoSaveIndicator_Control, ByteSnapshotEditorSession?>(nameof(Session));

    static readonly IReadOnlyList<Bitmap> Frames = LoadFrames();

    readonly DispatcherTimer animationTimer;
    readonly Border rootBorder;
    readonly Image indicatorImage;
    readonly TextBlock indicatorText;

    int frameIndex;
    ByteSnapshotEditorSession? subscribedSession;

    public ByteSnapshotEditorSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public AutoSaveIndicator_Control()
    {
        InitializeComponent();

        rootBorder = this.FindControl<Border>("RootBorder")!;
        indicatorImage = this.FindControl<Image>("IndicatorImage")!;
        indicatorText = this.FindControl<TextBlock>("IndicatorText")!;

        animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(88)
        };
        animationTimer.Tick += AnimationTimer_Tick;

        if (Frames.Count > 0)
        {
            indicatorImage.Source = Frames[0];
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SessionProperty)
        {
            UnsubscribeSession(subscribedSession);
            subscribedSession = change.NewValue as ByteSnapshotEditorSession;
            SubscribeSession(subscribedSession);
            UpdateVisualState();
        }
    }

    void SubscribeSession(ByteSnapshotEditorSession? session)
    {
        if (session != null)
        {
            session.PropertyChanged += Session_PropertyChanged;
        }
    }

    void UnsubscribeSession(ByteSnapshotEditorSession? session)
    {
        if (session != null)
        {
            session.PropertyChanged -= Session_PropertyChanged;
        }
    }

    void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ByteSnapshotEditorSession.IsSaveIndicatorVisible) ||
            e.PropertyName == nameof(ByteSnapshotEditorSession.SaveIndicatorText))
        {
            Dispatcher.UIThread.Post(UpdateVisualState);
        }
    }

    void UpdateVisualState()
    {
        bool isVisible = Session?.IsSaveIndicatorVisible == true;
        rootBorder.IsVisible = isVisible;
        indicatorText.Text = string.IsNullOrWhiteSpace(Session?.SaveIndicatorText)
            ? "Auto-save"
            : Session!.SaveIndicatorText;

        if (!isVisible)
        {
            animationTimer.Stop();
            frameIndex = 0;
            if (Frames.Count > 0)
            {
                indicatorImage.Source = Frames[0];
            }
            return;
        }

        if (Frames.Count == 0)
            return;

        if (!animationTimer.IsEnabled)
        {
            frameIndex = 0;
            indicatorImage.Source = Frames[frameIndex];
            animationTimer.Start();
        }
    }

    void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (Frames.Count == 0)
            return;

        frameIndex = (frameIndex + 1) % Frames.Count;
        indicatorImage.Source = Frames[frameIndex];
    }

    static IReadOnlyList<Bitmap> LoadFrames()
    {
        List<Bitmap> frames = new();

        for (int index = 0; index < 8; index++)
        {
            Uri uri = new($"avares://FFXProjectEditor/Assets/Autosave/chocobo_frame_{index}.png");
            using var stream = AssetLoader.Open(uri);
            frames.Add(new Bitmap(stream));
        }

        return frames;
    }

    public void Dispose()
    {
        animationTimer.Stop();
        animationTimer.Tick -= AnimationTimer_Tick;
        UnsubscribeSession(subscribedSession);
    }
}
