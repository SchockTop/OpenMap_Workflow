using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// Right-mouse orbit, middle-mouse pan, scroll zoom, F to frame the
    /// terrain — plus a follow mode that chases the aircraft. Attached to the
    /// main camera by the scene loader.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 focus;
        public float distance = 300f;
        public float yawDeg = 45f;
        public float pitchDeg = 40f;
        public Transform followTarget;
        public bool follow;
        public Bounds frameBounds;

        private const float OrbitSpeed = 0.25f;
        private const float MinPitch = -5f;
        private const float MaxPitch = 89f;

        public void FrameTerrain()
        {
            follow = false;
            focus = frameBounds.center;
            distance = Mathf.Max(frameBounds.extents.x, frameBounds.extents.z) * 1.8f + 10f;
            yawDeg = 45f;
            pitchDeg = 40f;
        }

        private void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F)) FrameTerrain();

            if (Input.GetMouseButton(1))
            {
                yawDeg += Input.GetAxis("Mouse X") * OrbitSpeed * 10f;
                pitchDeg = Mathf.Clamp(
                    pitchDeg - Input.GetAxis("Mouse Y") * OrbitSpeed * 10f, MinPitch, MaxPitch);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                distance = Mathf.Clamp(distance * (1f - scroll * 0.3f), 2f, 100000f);

            if (Input.GetMouseButton(2))
            {
                var pan = new Vector3(-Input.GetAxis("Mouse X"), 0f, -Input.GetAxis("Mouse Y"));
                focus += Quaternion.Euler(0f, yawDeg, 0f) * pan * (distance * 0.02f);
                follow = false;
            }

            if (follow && followTarget != null)
                focus = followTarget.position;

            var rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            transform.position = focus - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
