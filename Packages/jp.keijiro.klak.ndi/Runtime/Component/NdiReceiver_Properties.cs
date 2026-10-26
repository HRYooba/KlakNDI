using UnityEngine;

namespace Klak.Ndi {

public sealed partial class NdiReceiver : MonoBehaviour
{
    #region NDI source settings

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
    }

    #endregion
}

} // namespace Klak.Ndi
