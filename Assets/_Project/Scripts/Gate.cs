using UnityEngine;

public class Gate : MonoBehaviour
{
    public enum Op { AddYears, MultiplyYears, DivideYears, MultiplyPlanes }

    public Op op = Op.AddYears;
    public int value = 500;
    public TextMesh label;
    public Renderer panel;
    public Material goodMaterial, badMaterial;

    public bool IsGood => op switch
    {
        Op.AddYears => value >= 0,
        Op.DivideYears => value <= 1,
        _ => value >= 1,
    };

    // Runs in the editor too: change op/value in the Inspector and the gate updates live.
    public void OnValidate()
    {
        if (label) foreach (var mesh in label.GetComponentsInChildren<TextMesh>()) mesh.text = op switch
        {
            Op.AddYears => (value >= 0 ? "+" : "") + value + "\nYEARS",
            Op.MultiplyYears => "x" + value + "\nYEARS",
            Op.DivideYears => "÷" + value + "\nYEARS",
            _ => "x" + value + "\nPLANES",
        };
        if (panel && goodMaterial && badMaterial) panel.sharedMaterial = IsGood ? goodMaterial : badMaterial;
    }
}
