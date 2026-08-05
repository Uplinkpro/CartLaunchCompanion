using CartLaunchCompanion.Core.Input;

namespace CartLaunchCompanion.Desktop.Input;

internal sealed class SdlGamepadStateMapper
{
    private const int StickDeadZone = 18000;

    private SdlGamepadSnapshot _previous;
    private LauncherAction _heldDirection = LauncherAction.None;
    private DateTimeOffset _nextDirectionRepeat = DateTimeOffset.MinValue;

    public IReadOnlyList<LauncherAction> Map(
        bool confirm,
        bool back,
        bool trailer,
        bool dpadUp,
        bool dpadDown,
        bool dpadLeft,
        bool dpadRight,
        short leftX,
        short leftY,
        DateTimeOffset timestamp)
    {
        var up = dpadUp || leftY <= -StickDeadZone;
        var down = dpadDown || leftY >= StickDeadZone;
        var left = dpadLeft || leftX <= -StickDeadZone;
        var right = dpadRight || leftX >= StickDeadZone;

        var current = new SdlGamepadSnapshot(
            confirm,
            back,
            trailer,
            up,
            down,
            left,
            right);

        var actions = new List<LauncherAction>(4);

        AddRisingEdge(actions, current.Confirm, _previous.Confirm, LauncherAction.Confirm);
        AddRisingEdge(actions, current.Back, _previous.Back, LauncherAction.Back);
        AddRisingEdge(actions, current.Trailer, _previous.Trailer, LauncherAction.Trailer);

        var direction = ResolveDirection(current);
        AddDirection(actions, direction, timestamp);

        _previous = current;
        return actions;
    }

    public void Reset()
    {
        _previous = default;
        _heldDirection = LauncherAction.None;
        _nextDirectionRepeat = DateTimeOffset.MinValue;
    }

    private void AddDirection(
        List<LauncherAction> actions,
        LauncherAction direction,
        DateTimeOffset timestamp)
    {
        if (direction is LauncherAction.None)
        {
            _heldDirection = LauncherAction.None;
            _nextDirectionRepeat = DateTimeOffset.MinValue;
            return;
        }

        if (direction != _heldDirection)
        {
            _heldDirection = direction;
            _nextDirectionRepeat = timestamp.AddMilliseconds(360);
            actions.Add(direction);
            return;
        }

        if (timestamp < _nextDirectionRepeat)
            return;

        actions.Add(direction);
        _nextDirectionRepeat = timestamp.AddMilliseconds(125);
    }

    private static LauncherAction ResolveDirection(
        SdlGamepadSnapshot state)
    {
        if (state.Left)
            return LauncherAction.NavigateLeft;

        if (state.Right)
            return LauncherAction.NavigateRight;

        if (state.Up)
            return LauncherAction.NavigateUp;

        if (state.Down)
            return LauncherAction.NavigateDown;

        return LauncherAction.None;
    }

    private static void AddRisingEdge(
        List<LauncherAction> actions,
        bool current,
        bool previous,
        LauncherAction action)
    {
        if (current && !previous)
            actions.Add(action);
    }
}
