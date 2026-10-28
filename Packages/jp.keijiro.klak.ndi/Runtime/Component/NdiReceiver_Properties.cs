using UnityEngine;

namespace Klak.Ndi {

public sealed partial class NdiReceiver : MonoBehaviour
{
    #region NDI source settings

    // Comma-separated addresses of machines to query directly, shared by all
    // receivers and NdiFinder. Use it when multicast discovery doesn't reach
    // the senders (the same as the Extra IPs setting of NDI Access Manager).
    public static string extraIPs
      { get => SharedInstance.ExtraIPs;
        set => SharedInstance.ExtraIPs = value; }

    // Comma-separated addresses this receiver adds to the shared ones while
    // it's enabled. The finder queries all of them, so every receiver can
    // find the senders at any of these addresses.
    [SerializeField] string _extraIPs = null;
    string _extraIPsRuntime;

    public string receiverExtraIPs
      { get => _extraIPsRuntime;
        set => SetReceiverExtraIPs(value); }

    // The addresses are registered only between OnEnable and OnDisable, which
    // always come in pairs (OnValidate also runs on prefab assets).
    bool _extraIPsRegistered;

    void SetReceiverExtraIPs(string ips)
    {
        _extraIPs = _extraIPsRuntime = ips;
        if (_extraIPsRegistered) SharedInstance.SetReceiverExtraIPs(this, ips);
    }

    [SerializeField] string _ndiName = null;
    string _ndiNameRuntime;

    public string ndiName
      { get => _ndiNameRuntime;
        set => SetNdiName(value); }

    void SetNdiName(string name)
    {
        if (_ndiNameRuntime == name) return;
        _ndiName = _ndiNameRuntime = name;
        Restart();
    }

    #endregion

    #region Target settings

    [SerializeField] RenderTexture _targetTexture = null;

    public RenderTexture targetTexture
      { get => _targetTexture;
        set => _targetTexture = value; }

    [SerializeField] Renderer _targetRenderer = null;

    public Renderer targetRenderer
      { get => _targetRenderer;
        set => _targetRenderer = value; }

    [SerializeField] string _targetMaterialProperty = null;

    public string targetMaterialProperty
      { get => _targetMaterialProperty;
        set => _targetMaterialProperty = value; }

    // Mixes the received audio into an AudioSource on this GameObject.
    // The AudioSource is started automatically in Play Mode.
    [SerializeField] bool _receiveAudio = false;
    volatile bool _receiveAudioRuntime;

    public bool receiveAudio
      { get => _receiveAudioRuntime;
        set => SetReceiveAudio(value); }

    void SetReceiveAudio(bool enable)
    {
        if (_receiveAudioRuntime != enable) _audioBuffer.Clear();
        _receiveAudio = _receiveAudioRuntime = enable;
    }

    #endregion

    #region Runtime property

    public RenderTexture texture => _converter?.LastDecoderOutput;

    public string metadata { get; set; }

    public Interop.Recv internalRecvObject => _recv;

    // Number of frames decoded into the texture. It only increases, so
    // comparing it between updates tells whether the texture was renewed.
    public long decodedFrameCount { get; private set; }

    // Number of frames the decoder rejected (unsupported frame layouts). The
    // texture keeps the last decoded frame.
    public long rejectedFrameCount { get; private set; }

    // Invoked on the main thread for every captured video frame, including
    // the ones superseded by a newer frame within the same update. The frame
    // data is valid only during the call.
    public event System.Action<Interop.VideoFrame> videoFrameCaptured;

    #endregion

    #region Resources asset reference

    [SerializeField, HideInInspector] NdiResources _resources = null;

    public void SetResources(NdiResources resources)
      => _resources = resources;

    #endregion

    #region Editor change validation

    // Applies changes on the serialized fields to the runtime properties.
    // We use OnValidate on Editor, which also works as an initializer.
    // Player never call it, so we use Awake instead of it.

    #if UNITY_EDITOR
    void OnValidate()
    #else
    void Awake()
    #endif
    {
        ndiName = _ndiName;
        receiveAudio = _receiveAudio;
        receiverExtraIPs = _extraIPs;
    }

    #endregion
}

} // namespace Klak.Ndi
