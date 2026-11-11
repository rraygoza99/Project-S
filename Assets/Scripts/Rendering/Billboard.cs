using UnityEngine;

namespace Spellcaster
{
    /// <summary>
    /// Turns a quad to face the player camera. Cylindrical = rotate around Y only, so sprites
    /// stay upright when looking up/down (DOOM style). Spherical is used for glowing FX.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        public bool Cylindrical = true;

        void LateUpdate()
        {
            var player = Player.Instance;
            if (!player) return;
            Vector3 d = transform.position - player.Cam.transform.position;
            if (Cylindrical) d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
