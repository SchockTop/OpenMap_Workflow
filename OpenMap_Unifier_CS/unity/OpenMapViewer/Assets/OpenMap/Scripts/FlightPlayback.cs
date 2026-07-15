using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// Drives the aircraft along the loaded trajectory: play/pause, speed,
    /// scrubbing. Positions lerp, attitudes slerp (from source quaternions
    /// when the log carried them, else from yaw/pitch/roll). Times are
    /// normalized so the timeline always starts at 0.
    /// </summary>
    public class FlightPlayback : MonoBehaviour
    {
        public Transform aircraft;
        public bool playing = true;
        public bool loop = true;
        public float speed = 1f;

        private float[] _times;
        private Vector3[] _positions;
        private Quaternion[] _rotations;
        private float _time;

        public bool HasData { get { return _times != null && _times.Length >= 2; } }
        public float Duration { get { return HasData ? _times[_times.Length - 1] : 0f; } }

        public float TimeSeconds
        {
            get { return _time; }
            set { _time = Mathf.Clamp(value, 0f, Duration); Apply(); }
        }

        public void SetData(TrajectoryJson data)
        {
            var samples = data.samples;
            _times = new float[samples.Length];
            _positions = new Vector3[samples.Length];
            _rotations = new Quaternion[samples.Length];
            float t0 = samples.Length > 0 ? samples[0].t : 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                _times[i] = s.t - t0;
                _positions[i] = OpenMapFrames.EnuToUnity(s.x, s.y, s.z);
                _rotations[i] = s.hasQuat
                    ? OpenMapFrames.EnuQuatToUnity(s.qx, s.qy, s.qz, s.qw)
                    : OpenMapFrames.AttitudeToUnity(s.yawDeg, s.pitchDeg, s.rollDeg);
            }
            _time = 0f;
            Apply();
        }

        public void SamplePose(float t, out Vector3 position, out Quaternion rotation)
        {
            if (!HasData)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            if (t <= _times[0]) { position = _positions[0]; rotation = _rotations[0]; return; }
            int last = _times.Length - 1;
            if (t >= _times[last]) { position = _positions[last]; rotation = _rotations[last]; return; }

            int hi = 1;
            while (_times[hi] < t) hi++; // samples are short arrays; linear is fine
            int lo = hi - 1;
            float k = (t - _times[lo]) / (_times[hi] - _times[lo]);
            position = Vector3.Lerp(_positions[lo], _positions[hi], k);
            rotation = Quaternion.Slerp(_rotations[lo], _rotations[hi], k);
        }

        public Vector3[] PathPositions()
        {
            return _positions != null ? (Vector3[])_positions.Clone() : new Vector3[0];
        }

        private void Update()
        {
            if (!HasData || !playing) return;
            _time += Time.deltaTime * speed;
            if (_time > Duration)
                _time = loop ? _time % Duration : Duration;
            Apply();
        }

        private void Apply()
        {
            if (!HasData || aircraft == null) return;
            Vector3 pos;
            Quaternion rot;
            SamplePose(_time, out pos, out rot);
            aircraft.position = pos;
            aircraft.rotation = rot;
        }
    }
}
