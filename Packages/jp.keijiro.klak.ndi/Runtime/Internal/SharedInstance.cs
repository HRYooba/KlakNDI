namespace Klak.Ndi {

// A wrangler class managing singleton-like shared object instances
static class SharedInstance
{
    #region Public properties

    // NDI find object
    static public Interop.Find Find
      => _find ?? InitializeFind();

    // Comma-separated addresses the find object queries directly. Changing
    // it recreates the find object on the next access.
    static public string ExtraIPs
      { get => _extraIPs;
        set => SetExtraIPs(value); }

    // NDI send object for the game view
    static public Interop.Send GameViewSend
      => _gameViewSend ?? InitializeGameViewSend();

    static public bool IsGameViewSend(Interop.Send send)
      => send != null && send == _gameViewSend;

    // Guards NdiSender instances against SendAudio calls from the audio
    // thread. It's shared because Game View senders share one NDI source.
    static public readonly object SendLock = new object();

    #endregion

    #region Shared object implementation

    static Interop.Find _find;
    static string _extraIPs;

    static Interop.Find InitializeFind()
    {
        _find = Interop.Find.Create(_extraIPs);
        SetFinalizer();
        return _find;
    }

    static void SetExtraIPs(string extraIPs)
    {
        if (_extraIPs == extraIPs) return;
        _extraIPs = extraIPs;
        _find?.Dispose();
        _find = null;
    }

    static Interop.Send _gameViewSend;

    static Interop.Send InitializeGameViewSend()
    {
        _gameViewSend = Interop.Send.Create("Game View");
        SetFinalizer();
        return _gameViewSend;
    }

    #endregion

    #region Initializer implementation

    static SharedInstance()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        AndroidHelper.SetupNetwork();
        #endif
    }

    #endregion

    #region Finalizer implementatioin

    // We have to clean up the shared objects on a domain reload.
    // (This happens only on Editor.)

    #if UNITY_EDITOR

    static bool _finalizerReady;

    static void SetFinalizer()
    {
        if (_finalizerReady) return;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnDomainReload;
        _finalizerReady = true;
    }

    static void OnDomainReload()
    {
        _find?.Dispose();
        _find = null;

        lock (SendLock)
        {
            _gameViewSend?.Dispose();
            _gameViewSend = null;
        }
    }

    #else

    static void SetFinalizer() {}

    #endif

    #endregion
}

} // namespace Klak.Ndi
