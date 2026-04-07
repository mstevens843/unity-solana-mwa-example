using UnityEngine;
using TMPro;

/// <summary>
/// Simple status display component that listens to MWAManager status updates.
/// Attach to any GameObject with a TextMeshProUGUI component.
/// </summary>
public class StatusDisplay : MonoBehaviour
{
    public TextMeshProUGUI statusText;

    private void OnEnable()
    {
        if (MWAManager.Instance != null)
            MWAManager.Instance.OnStatusUpdated += UpdateText;
    }

    private void OnDisable()
    {
        if (MWAManager.Instance != null)
            MWAManager.Instance.OnStatusUpdated -= UpdateText;
    }

    private void UpdateText(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
