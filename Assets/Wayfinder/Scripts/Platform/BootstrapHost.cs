using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wayfinder.Platform
{
    /// <summary>Prototype entry point. Campaign composition remains a separate milestone.</summary>
    public sealed class BootstrapHost : MonoBehaviour
    {
        void Start()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = .02f;
            QualityTierService.Apply(Application.isMobilePlatform ? QualityTier.BatteryLow : QualityTier.Balanced);
            SceneManager.LoadScene("Title");
        }
    }
}
