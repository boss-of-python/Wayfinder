using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wayfinder.Presentation
{
    /// <summary>Explicitly labeled prototype entry; does not pretend campaign/Continue exists.</summary>
    public sealed class TitleFlowHost : MonoBehaviour
    {
        void Start()
        {
            Time.timeScale = 1f;
            var root = PrototypeUi.CreateRoot("Title UI");
            PrototypeUi.Label(root, "WAYFINDER\nThe Quiet Frontier", new Vector2(.1f, .64f), new Vector2(.9f, .9f), 42);
            PrototypeUi.Label(root, "Driving prototype — greybox assets\nCampaign and saves are not connected yet.",
                new Vector2(.1f, .44f), new Vector2(.9f, .63f));
            PrototypeUi.Button(root, "Open proving ground", new Vector2(.3f, .27f), new Vector2(.7f, .40f))
                .onClick.AddListener(() => SceneManager.LoadScene("PhysicsTestGround"));
            PrototypeUi.Label(root, "Offline • No ads • No purchases", new Vector2(.1f, .08f), new Vector2(.9f, .20f));
        }
    }
}
