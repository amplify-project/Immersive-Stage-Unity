using System.Collections;
using UnityEngine;

// SELF-CONTAINED speaker spatial-audio test. No media files, no dependency on
// PlatformManager / the concert scene, so a clean reinstall can't break it.
//
// HOW TO USE:
//   1. New empty scene (or any scene). Create an empty GameObject.
//   2. Add this component to it. (It adds its own AudioListener if the scene has none.)
//   3. Build And Run on the Pico and listen over the built-in speakers.
//
// WHAT IT DOES (pure Unity 3D panning — the robust, speaker-friendly path):
//   - A 440 Hz tone fixed to your LEFT.
//   - A 660 Hz tone fixed to your RIGHT.
//   - An 880 Hz tone ORBITING around your head.
// If panning works over the speakers you'll clearly hear low-pitch-left,
// mid-pitch-right, and the high tone sweeping side to side. That tells us whether
// the Pico near-ear speakers reproduce left/right at all — if yes, plain panning is
// a solid option for the concert and we don't need the risky transaural path.
public class SpatialSpeakerTest : MonoBehaviour
{
    AudioSource m_Orbit;
    const float Radius = 8f;

    void Start()
    {
        if (FindObjectOfType<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>(); // listener at this GO, facing +Z

        MakeSource("ToneLeft",  new Vector3(-Radius, 0f, 0f), Tone(440f));
        MakeSource("ToneRight", new Vector3( Radius, 0f, 0f), Tone(660f));
        m_Orbit = MakeSource("ToneOrbit", new Vector3(0f, 0f, Radius), Tone(880f));

        Debug.Log("[SpkTest] Started: 440Hz LEFT, 660Hz RIGHT, 880Hz ORBITING. " +
                  "Listen for left/right separation over the speakers.");
        StartCoroutine(Orbit());
    }

    AudioSource MakeSource(string name, Vector3 pos, AudioClip clip)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.spatialBlend = 1f;                  // full 3D panning (left/right by position)
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 1f;
        s.maxDistance = 10000f;               // no distance ducking at these ranges
        s.volume = 0.6f;
        s.Play();
        return s;
    }

    // 1-second looping sine tone at the given frequency.
    AudioClip Tone(float freq)
    {
        const int sr = 44100;
        int len = sr;                          // 1 s, loops seamlessly (integer cycles ~ fine)
        var data = new float[len];
        for (int i = 0; i < len; i++)
            data[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * freq * i / sr);
        var clip = AudioClip.Create("tone" + Mathf.RoundToInt(freq), len, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Sweep the 880 Hz tone around the head so movement is obvious; log the side.
    IEnumerator Orbit()
    {
        float a = 0f;
        float logT = 0f;
        while (true)
        {
            a += 40f * Time.deltaTime;         // 40 deg/sec
            float rad = a * Mathf.Deg2Rad;
            m_Orbit.transform.localPosition = new Vector3(Mathf.Sin(rad) * Radius, 0f, Mathf.Cos(rad) * Radius);

            logT += Time.deltaTime;
            if (logT >= 1f)
            {
                logT = 0f;
                float x = m_Orbit.transform.localPosition.x;
                string side = x > 1f ? "RIGHT" : x < -1f ? "LEFT" : "center";
                Debug.Log($"[SpkTest] orbit 880Hz at x={x:F1} ({side})");
            }
            yield return null;
        }
    }
}
