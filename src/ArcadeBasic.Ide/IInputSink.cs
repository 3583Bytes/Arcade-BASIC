namespace ArcadeBasic.Ide;

/// <summary>
/// A surface that can collect a line of INPUT from the user. Both the text
/// <see cref="OutputPane"/> and the graphics <see cref="GraphicsPane"/> provide
/// their own input field, so an interactive program reads input on whichever
/// surface it is using without the view jumping between tabs.
/// </summary>
internal interface IInputSink
{
    /// <summary>Activate the input field; invoke <paramref name="onComplete"/>
    /// with the submitted text, or <c>null</c> if the read is cancelled.</summary>
    void BeginRead(Action<string?> onComplete);

    /// <summary>Cancel a pending read (invokes its callback with <c>null</c>).</summary>
    void CancelRead();
}
