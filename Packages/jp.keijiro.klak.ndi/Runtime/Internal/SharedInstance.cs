namespace Klak.Ndi {

// A wrangler class managing singleton-like shared object instances
static class SharedInstance
{
    #region Public properties

    // NDI find object
    // It queries the static extra IPs and the ones of the enabled receivers
    // directly. When the combined addresses differ from the ones the current
    // find object was created with, it's recreated on access.
    static public Interop.Find Find
      => (_find != null && !UpdateCombinedExtraIPs()) ? _find : InitializeFind();

    // Comma-separated addresses shared by all receivers and NdiFinder
    static public string ExtraIPs
      { get => _extraIPs;
        set => SetExtraIPs(value); }

    // Comma-separated addresses contributed by a receiver (null to remove)
    static public void SetReceiverExtraIPs(object owner, string extraIPs)
    {
        if (string.IsNullOrEmpty(extraIPs))
        {
            if (_receiverExtraIPs.Remove(owner)) _extraIPsDirty = true;
        }
        else if (!_receiverExtraIPs.TryGetValue(owner, out var current) ||
                 current != extraIPs)
        {
            _receiverExtraIPs[owner] = extraIPs;
            _extraIPsDirty = true;
        }
    }

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
    static readonly System.Collections.Generic.Dictionary<object, string>
      _receiverExtraIPs = new System.Collections.Generic.Dictionary<object, string>();
    static bool _extraIPsDirty = true;
    static string _combinedExtraIPs;
    static string _findExtraIPs;

    static Interop.Find InitializeFind()
    {
        UpdateCombinedExtraIPs();
        _find?.Dispose();
        _find = Interop.Find.Create(_combinedExtraIPs);
        _findExtraIPs = _combinedExtraIPs;
        SetFinalizer();
        return _find;
    }

    static void SetExtraIPs(string extraIPs)
    {
        if (_extraIPs == extraIPs) return;
        _extraIPs = extraIPs;
        _extraIPsDirty = true;
    }

    // Recombines the addresses if they were changed. Returns true when the
    // result differs from the addresses of the current find object.
    static bool UpdateCombinedExtraIPs()
    {
        if (_extraIPsDirty)
        {
            var list = new System.Collections.Generic.SortedSet<string>
              (System.StringComparer.Ordinal);
            AddAddresses(list, _extraIPs);
            foreach (var ips in _receiverExtraIPs.Values)
                AddAddresses(list, ips);
            _combinedExtraIPs = list.Count > 0 ? string.Join(",", list) : null;
            _extraIPsDirty = false;
        }
        return _combinedExtraIPs != _findExtraIPs;
    }

    static void AddAddresses
      (System.Collections.Generic.SortedSet<string> list, string ips)
    {
        if (string.IsNullOrEmpty(ips)) return;
        foreach (var ip in ips.Split(','))
            if (ip.Trim().Length > 0) list.Add(ip.Trim());
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
