using UnityEngine;

namespace Gaze.Core
{
    public class MusicianReticle : MonoBehaviour
    {
        [SerializeField] float angularScale = 0.025f;
        [SerializeField] Color reticleColor = Color.white;

        void Awake()
        {
            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = BuildSprite();
            sr.color = reticleColor;
            sr.sortingOrder = 10;
            gameObject.SetActive(false);
        }

        public void SetState(Vector3 worldPos, Camera cam)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.position = worldPos;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(worldPos - cam.transform.position);
            float dist = Vector3.Distance(worldPos, cam.transform.position);
            transform.localScale = Vector3.one * (dist * angularScale);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        Sprite BuildSprite()
        {
            const int size = 128;
            const float cx = size / 2f;
            const float cy = size / 2f;
            const float radius = 42f;
            const float thickness = 7f;
            const float halfSpan = 35f;
            float[] arcCenters = { 45f, 135f, 225f, 315f };
            const float dotR = 3.5f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var c = (Color32)reticleColor;

            foreach (float ac in arcCenters)
            {
                float a0 = (ac - halfSpan) * Mathf.Deg2Rad;
                float a1 = (ac + halfSpan) * Mathf.Deg2Rad;
                for (float a = a0; a <= a1; a += 0.3f * Mathf.Deg2Rad)
                    for (float r = radius - thickness / 2f; r <= radius + thickness / 2f; r += 0.4f)
                    {
                        int px = Mathf.RoundToInt(cx + Mathf.Cos(a) * r);
                        int py = Mathf.RoundToInt(cy + Mathf.Sin(a) * r);
                        if ((uint)px < size && (uint)py < size)
                            pixels[py * size + px] = c;
                    }
            }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= dotR * dotR)
                        pixels[y * size + x] = c;
                }

            tex.SetPixels32(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        }
    }
}
