using UnityEngine;

namespace Klak.Ndi {

//
// Audio capture component
//
// Forwards the audio passing through this GameObject to an NdiSender.
// Attach it to the GameObject with the AudioListener to send the final mix,
// or to a GameObject with an AudioSource to send that source only.
// The audio itself is passed through unchanged.
//
public sealed class NdiAudioCapture : MonoBehaviour
{
    #region Editable properties

    [SerializeField] NdiSender _sender = null;

    public NdiSender sender
      { get => _sender;
        set => _target = _sender = value; }

    #endregion

    #region Private members

    // OnAudioFilterRead runs on the audio thread and can't touch Unity
    // objects, so the target and the sample rate are copied here.
    volatile NdiSender _target;
    volatile int _sampleRate;

    void UpdateSampleRate(bool deviceWasChanged = false)
      => _sampleRate = AudioSettings.outputSampleRate;

    #endregion

    #region MonoBehaviour implementation

    void OnEnable()
    {
        UpdateSampleRate();
        AudioSettings.OnAudioConfigurationChanged += UpdateSampleRate;
        _target = _sender;
    }

    void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= UpdateSampleRate;
        _target = null;
    }

    void OnAudioFilterRead(float[] data, int channels)
      => _target?.SendAudio(data, channels, _sampleRate);

    #endregion
}

} // namespace Klak.Ndi
