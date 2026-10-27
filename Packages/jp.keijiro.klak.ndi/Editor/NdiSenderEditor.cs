using UnityEngine;
using UnityEditor;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiSender))]
sealed class NdiSenderEditor : UnityEditor.Editor
{
    static class Labels
    {
        public static Label NdiName = "NDI Name";
    }

    static readonly string AudioOnlyNote =
      "Sends only the audio passed to SendAudio (for example, by an NDI " +
      "Audio Capture). The NDI source appears in Play Mode.";

    #pragma warning disable CS0649

    AutoProperty _ndiName;
    AutoProperty _keepAlpha;
    AutoProperty _captureMethod;
    AutoProperty _sourceCamera;
    AutoProperty _sourceTexture;
    AutoProperty _sourceRect;

    #pragma warning restore

    void OnEnable() => AutoProperty.Scan(this);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // NDI Name
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex != (int)CaptureMethod.GameView)
            EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        // Keep Alpha
        EditorGUILayout.PropertyField(_keepAlpha);

        // Capture Method
        EditorGUILayout.PropertyField(_captureMethod);

        EditorGUI.indentLevel++;

        // Source Camera
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Camera)
            EditorGUILayout.PropertyField(_sourceCamera);

        // Source Texture / Source Rect
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Texture)
        {
            EditorGUILayout.PropertyField(_sourceTexture);
            EditorGUILayout.PropertyField(_sourceRect);
        }

        // Audio Only
        if (!_captureMethod.Target.hasMultipleDifferentValues &&
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.AudioOnly)
            EditorGUILayout.HelpBox(AudioOnlyNote, MessageType.None);

        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}

} // namespace Klak.Ndi.Editor
