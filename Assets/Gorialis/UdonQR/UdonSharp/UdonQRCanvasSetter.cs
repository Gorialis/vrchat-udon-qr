
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using System;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UdonSharpEditor;
#endif

public class UdonQRCanvasSetter : UdonSharpBehaviour
{
    public UdonQR _qrLibrary;
    public Text _canvasTarget;
    public int errorCorrection = 0;
    public int maskPattern = 1;

    public void Set(string text)
    {
        _canvasTarget.text = _qrLibrary.Create(text, errorCorrection, maskPattern, "\u2588", "\u2591");
    }

    void Start()
    {

    }
}

#if !COMPILER_UDONSHARP && UNITY_EDITOR

[CustomEditor(typeof(UdonQRCanvasSetter))]
public class UdonQRCanvasSetterEditor : Editor
{
    private string inputString = "";

    public override void OnInspectorGUI()
    {
        // Draws the default convert to UdonBehaviour button, program asset field, sync settings, etc.
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        UdonQRCanvasSetter inspectorBehaviour = (UdonQRCanvasSetter)target;

        Undo.RecordObject(inspectorBehaviour, "Edited UdonQRCanvasSetter settings");
        PrefabUtility.RecordPrefabInstancePropertyModifications(inspectorBehaviour);

        EditorGUILayout.LabelField("UdonQR library object:");
        inspectorBehaviour._qrLibrary = (UdonQR)EditorGUILayout.ObjectField(inspectorBehaviour._qrLibrary, typeof(UdonQR), true);

        EditorGUILayout.LabelField("Target canvas for QR code:");
        inspectorBehaviour._canvasTarget = (Text)EditorGUILayout.ObjectField(inspectorBehaviour._canvasTarget, typeof(Text), true);

        int[] errorCorrectionIdentifiers = { 1, 0, 3, 2 };
        string[] errorCorrectionLiterals = { "Low (L)", "Medium (M)", "Medium-High (Q)", "High (H)" };

        inspectorBehaviour.errorCorrection = EditorGUILayout.IntPopup("Error correction level", inspectorBehaviour.errorCorrection, errorCorrectionLiterals, errorCorrectionIdentifiers);

        int[] maskPatternIdentifiers = { 0, 1, 2, 3, 4, 5, 6, 7 };
        string[] maskPatternLiterals = { "0 ((i + j) % 2 = 0)", "1 (i % 2 = 0)", "2 (j % 3 = 0)", "3 ((i + j) % 3 = 0)", "4 ((i / 2 + j / 3) % 2 = 0)", "5 ((i * j) % 2 + (i * j) % 3 = 0)", "6 (((i + j) % 3 + ((i + j) % 2)) % 2 = 0)", "7 (((i + j) % 2 + ((i + j) % 2)) % 3 = 0)" };

        inspectorBehaviour.maskPattern = EditorGUILayout.IntPopup("Mask pattern", inspectorBehaviour.maskPattern, maskPatternLiterals, maskPatternIdentifiers);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Editor only", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Input for QR");
        inputString = EditorGUILayout.TextArea(inputString);

        if (GUILayout.Button("Set to value"))
        {
            if (inspectorBehaviour._qrLibrary == null)
                EditorUtility.DisplayDialog("Error", "You need to set a library object.", "OK");
            else
                inspectorBehaviour.Set(inputString);
        }
    }
}
#endif
