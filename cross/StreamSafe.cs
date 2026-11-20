namespace CapriDesktop
{
    // Linux and Mac stand-in for app/StreamSafe.cs. The Windows file draws its stand-in icons with System.Drawing, which
    // these builds do not have, but the shared Hands.cs still asks whether stream-safe is on before reading a window title
    // out. Nothing here turns it on yet, so it stays off, which is how the Windows build starts too.
    internal static class StreamSafe
    {
        internal static volatile bool On;
    }
}
