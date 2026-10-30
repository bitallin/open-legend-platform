#if UNITY_2019_1_OR_NEWER
using UnityEngine;

namespace Legend.Client.Combat
{
    public sealed class LegendDamagePopup : MonoBehaviour
    {
        private TextMesh _text = null!;
        private float _life = 1.05f;
        private Vector3 _velocity;

        public static void Spawn(Vector3 worldPos, string text, Color color)
        {
            var go = new GameObject("DmgPopup");
            go.transform.position = worldPos + Vector3.up * 1.75f;
            var popup = go.AddComponent<LegendDamagePopup>();
            popup._text = go.AddComponent<TextMesh>();
            popup._text.text = text;
            popup._text.color = color;
            popup._text.characterSize = 0.22f;
            popup._text.fontSize = 56;
            popup._text.fontStyle = FontStyle.Bold;
            popup._text.anchor = TextAnchor.MiddleCenter;
            popup._velocity = new Vector3(Random.Range(-0.35f, 0.35f), 2.1f, 0f);
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;
            _velocity.y -= 2.8f * Time.deltaTime;
            if (Camera.main != null)
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            if (_text != null)
            {
                var c = _text.color;
                c.a = Mathf.Clamp01(_life / 0.45f);
                _text.color = c;
            }
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
#endif
