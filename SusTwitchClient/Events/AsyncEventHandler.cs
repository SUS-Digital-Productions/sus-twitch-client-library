namespace SusTwitchClient.Events;

/// <summary>
/// Async event handler delegate.
/// </summary>
public delegate Task AsyncEventHandler<TSender, TArgs>(TSender sender, TArgs args)
    where TArgs : EventArgs;
