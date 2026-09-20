using System;
using System.Collections.Generic;
using System.Linq;
using UILib;
using UILib.ContextMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Timeline
{
    public partial class Timeline
    {
        #region Trim - Private Variables
        private const float _trimTimeEpsilon = 0.0001f;
        private const float _trimMinLength = 0.01f;
        private static readonly Color _trimRangeFillColor = new Color(1f, 0.72f, 0.1f, 0.18f);
        private static readonly Color _trimRangeEdgeColor = new Color(1f, 0.72f, 0.1f, 0.9f);

        private RectTransform _trimRangeOverlay;
        private bool _hasTrimRange;
        private bool _isTrimRangeSelecting;
        private bool _trimRangeDragging;
        private bool _trimDisabled;
        private float _trimRangeAnchor;
        private float _trimRangeStart;
        private float _trimRangeEnd;
        #endregion

        #region Trim - Range UI
        private void InitTrimRange()
        {
            Image fill = UIUtility.CreateImage("Trim Range", _grid);
            fill.sprite = null;
            fill.color = _trimRangeFillColor;
            fill.raycastTarget = false;
            _trimRangeOverlay = fill.rectTransform;
            _trimRangeOverlay.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _trimRangeOverlay.anchorMin = new Vector2(0f, 0f);
            _trimRangeOverlay.anchorMax = new Vector2(0f, 1f);
            _trimRangeOverlay.pivot = new Vector2(0f, 0.5f);
            _trimRangeOverlay.offsetMin = Vector2.zero;
            _trimRangeOverlay.offsetMax = Vector2.zero;

            for (int i = 0; i < 2; i++)
            {
                Image edge = UIUtility.CreateImage(i == 0 ? "Start" : "End", _trimRangeOverlay);
                edge.sprite = null;
                edge.color = _trimRangeEdgeColor;
                edge.raycastTarget = false;
                RectTransform rt = edge.rectTransform;
                rt.anchorMin = new Vector2(i, 0f);
                rt.anchorMax = new Vector2(i, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, 0f);
                rt.anchoredPosition = Vector2.zero;
            }

            // Below the playback cursor so the cursor stays visible.
            _trimRangeOverlay.SetSiblingIndex(_cursor.GetSiblingIndex());
            _trimRangeOverlay.gameObject.SetActive(false);
        }

        private void UpdateTrimRangeOverlay()
        {
            if (_trimRangeOverlay == null || _trimDisabled)
                return;

            // Ctrl+click without drag (no drag events fired): finish the selection when the button is released.
            if (_isTrimRangeSelecting && Input.GetMouseButton(0) == false)
                FinishTrimRangeSelect();

            bool show = _hasTrimRange || _isTrimRangeSelecting;
            if (_trimRangeOverlay.gameObject.activeSelf != show)
                _trimRangeOverlay.gameObject.SetActive(show);
            if (show == false)
                return;

            float x0 = TrimTimeToGridX(_trimRangeStart);
            float x1 = TrimTimeToGridX(_trimRangeEnd);
            _trimRangeOverlay.offsetMin = new Vector2(x0, 0f);
            _trimRangeOverlay.offsetMax = new Vector2(Mathf.Max(x1, x0 + 1f), 0f);
        }

        private float TrimTimeToGridX(float time)
        {
            // Same convention as the playback cursor, which is anchored to the left edge of the grid.
            return _duration > 0f ? time * _grid.rect.width / _duration : 0f;
        }

        private void OnGridTopPointerDown(PointerEventData eventData)
        {
            _trimRangeDragging = false;
            if (_trimDisabled || _trimRangeOverlay == null)
            {
                OnGridTopMouse(eventData);
                return;
            }
            try
            {
                _trimRangeDragging = eventData.button == PointerEventData.InputButton.Left && IsTrimSelectModifierHeld();
                if (_trimRangeDragging)
                {
                    BeginTrimRangeSelect(eventData);
                    return;
                }
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    ShowTrimContextMenu(eventData);
                    return;
                }
            }
            catch (Exception e)
            {
                _trimRangeDragging = false;
                _trimDisabled = true;
                ClearTrimRange();
                Logger.LogError("Trim: error on the time bar, the trim feature is disabled\n" + e);
            }
            OnGridTopMouse(eventData);
        }

        private static bool IsTrimSelectModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private bool TryGetGridTopTime(PointerEventData eventData, out float time)
        {
            time = 0f;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_gridTop, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                return false;
            time = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
            if (Input.GetKey(KeyCode.LeftShift))
            {
                float beat = _blockLength / _divisions;
                float mod = time % beat;
                if (mod / beat > 0.5f)
                    time += beat - mod;
                else
                    time -= mod;
            }
            time = Mathf.Clamp(time, 0f, _duration);
            return true;
        }

        private void BeginTrimRangeSelect(PointerEventData eventData)
        {
            if (!TryGetGridTopTime(eventData, out float time))
                return;
            _isTrimRangeSelecting = true;
            _hasTrimRange = false;
            _trimRangeAnchor = time;
            _trimRangeStart = time;
            _trimRangeEnd = time;
            UpdateTrimRangeOverlay();
        }

        private void UpdateTrimRangeSelect(PointerEventData eventData)
        {
            if (!TryGetGridTopTime(eventData, out float time))
                return;
            _trimRangeStart = Mathf.Min(_trimRangeAnchor, time);
            _trimRangeEnd = Mathf.Max(_trimRangeAnchor, time);
            UpdateTrimRangeOverlay();
        }

        private void EndTrimRangeSelect(PointerEventData eventData)
        {
            UpdateTrimRangeSelect(eventData);
            FinishTrimRangeSelect();
        }

        private void FinishTrimRangeSelect()
        {
            _isTrimRangeSelecting = false;
            if (_trimRangeEnd - _trimRangeStart >= _trimMinLength)
                SetTrimRange(_trimRangeStart, _trimRangeEnd);
            else
                ClearTrimRange();
        }

        private void SetTrimRange(float start, float end)
        {
            if (end < start)
            {
                float tmp = start;
                start = end;
                end = tmp;
            }
            start = Mathf.Max(0f, start);
            if (end - start < _trimMinLength)
            {
                ClearTrimRange();
                Logger.LogMessage("Trim range is too short");
                return;
            }
            _hasTrimRange = true;
            _trimRangeStart = start;
            _trimRangeEnd = end;
            UpdateTrimRangeOverlay();
        }

        private void SetTrimRangeEdge(bool isStart, float time)
        {
            time = Mathf.Clamp(time, 0f, _duration);
            float start = _hasTrimRange ? _trimRangeStart : 0f;
            float end = _hasTrimRange ? _trimRangeEnd : _duration;
            if (isStart)
                start = time;
            else
                end = time;
            SetTrimRange(start, end);
        }

        private void ClearTrimRange()
        {
            _hasTrimRange = false;
            _isTrimRangeSelecting = false;
            if (_trimRangeOverlay != null)
                _trimRangeOverlay.gameObject.SetActive(false);
        }

        private float GetCursorTime()
        {
            float time = _playbackTime % _duration;
            if (time == 0f && _playbackTime == _duration)
                time = _duration;
            return time;
        }

        private static string FormatTrimTime(float time)
        {
            return $"{Mathf.FloorToInt(time / 60):00}:{(time % 60):00.000}";
        }

        private void ShowTrimContextMenu(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_ui.transform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                return;

            List<AContextMenuElement> elements = new List<AContextMenuElement>();
            if (_hasTrimRange)
            {
                elements.Add(new LeafElement()
                {
                    icon = _deleteSprite,
                    text = $"Trim to {FormatTrimTime(_trimRangeStart)} - {FormatTrimTime(_trimRangeEnd)}",
                    onClick = p => RequestTrim(true)
                });
                elements.Add(new LeafElement()
                {
                    icon = _deleteSprite,
                    text = "Trim (keep original times)",
                    onClick = p => RequestTrim(false)
                });
            }
            elements.Add(new LeafElement()
            {
                icon = _chevronDownSprite,
                text = "Set trim start at cursor",
                onClick = p => SetTrimRangeEdge(true, GetCursorTime())
            });
            elements.Add(new LeafElement()
            {
                icon = _chevronDownSprite,
                text = "Set trim end at cursor",
                onClick = p => SetTrimRangeEdge(false, GetCursorTime())
            });
            if (_selectedKeyframes.Count > 1)
            {
                elements.Add(new LeafElement()
                {
                    icon = _selectAllSprite,
                    text = "Trim range from selected keyframes",
                    onClick = p => SetTrimRange(_selectedKeyframes.Min(k => k.Key), _selectedKeyframes.Max(k => k.Key))
                });
            }
            if (_hasTrimRange)
            {
                elements.Add(new LeafElement()
                {
                    icon = _checkboxSprite,
                    text = "Clear trim range",
                    onClick = p => ClearTrimRange()
                });
            }
            UIUtility.ShowContextMenu(_ui, localPoint, elements, 240);
        }

        private void RequestTrim(bool moveToStart)
        {
            if (!_hasTrimRange)
                return;
            float start = _trimRangeStart;
            float end = _trimRangeEnd;
            string message = moveToStart
                ? $"Trim the timeline to {FormatTrimTime(start)} - {FormatTrimTime(end)}?\nEverything before and after this range will be deleted, the range will start at 00:00 and the duration will be set to {FormatTrimTime(end - start)}."
                : $"Delete all keyframes before {FormatTrimTime(start)} and after {FormatTrimTime(end)}?";
            UIUtility.DisplayConfirmationDialog(result =>
            {
                if (result)
                    TrimTimeline(start, end, moveToStart);
            }, message);
        }
        #endregion

        #region Trim - Logic
        /// <summary>
        /// Deletes every keyframe outside [start, end]. Keyframes are inserted on the range edges where an animation
        /// crosses them (with a split curve) so the kept part plays exactly like before.
        /// </summary>
        private void TrimTimeline(float start, float end, bool moveToStart)
        {
            if (end - start < _trimMinLength)
                return;

            isPlaying = false;
            float previousTime = _playbackTime;
            List<Interpolable> interpolables = _interpolables.Values.ToList();

            List<Interpolable> needStart = new List<Interpolable>();
            List<Interpolable> needEnd = new List<Interpolable>();
            foreach (Interpolable interpolable in interpolables)
            {
                IList<float> times = interpolable.keyframes.Keys;
                if (times.Count == 0)
                    continue;
                if (times[0] < start - _trimTimeEpsilon && !HasKeyframeNear(times, start))
                    needStart.Add(interpolable);
                if (times[times.Count - 1] > end + _trimTimeEpsilon && !HasKeyframeNear(times, end))
                    needEnd.Add(interpolable);
            }

            Dictionary<Interpolable, object> startValues = SampleInterpolableValues(start, needStart);
            Dictionary<Interpolable, object> endValues = SampleInterpolableValues(end, needEnd);

            int removed = 0;
            int added = 0;
            List<Interpolable> emptied = new List<Interpolable>();
            foreach (Interpolable interpolable in interpolables)
            {
                if (interpolable.keyframes.Count == 0)
                    continue;
                try
                {
                    TrimInterpolable(interpolable, start, end, moveToStart, startValues, endValues, ref removed, ref added);
                }
                catch (Exception e)
                {
                    Logger.LogError($"Trim: couldn't trim interpolable \"{interpolable}\"\n{e}");
                }
                if (interpolable.keyframes.Count == 0)
                    emptied.Add(interpolable);
            }

            _selectedKeyframes.Clear();
            CloseKeyframeWindow();
            if (emptied.Count != 0)
                RemoveInterpolables(emptied);

            float newTime = previousTime;
            if (moveToStart)
            {
                _duration = end - start;
                newTime = Mathf.Clamp(previousTime - start, 0f, _duration);
            }
            ClearTrimRange();
            UpdateGrid();
            ApplyPlaybackTime(newTime);

            Logger.LogMessage($"Timeline trimmed: {removed} keyframe(s) removed, {added} edge keyframe(s) added");
        }

        private void TrimInterpolable(Interpolable interpolable, float start, float end, bool moveToStart, Dictionary<Interpolable, object> startValues, Dictionary<Interpolable, object> endValues, ref int removed, ref int added)
        {
            List<KeyValuePair<float, Keyframe>> original = interpolable.keyframes.ToList();
            int count = original.Count;

            int lastBeforeStart = original.FindLastIndex(k => k.Key < start);
            int firstAfterStart = original.FindIndex(k => k.Key > start);
            int lastBeforeEnd = original.FindLastIndex(k => k.Key < end);
            int firstAfterEnd = original.FindIndex(k => k.Key > end);

            bool addStart = startValues.TryGetValue(interpolable, out object startValue);
            bool addEnd = endValues.TryGetValue(interpolable, out object endValue);

            // Capture original curves first, they may be replaced below.
            AnimationCurve[] originalCurves = original.Select(k => k.Value.curve).ToArray();

            Keyframe startKeyframe = null;
            if (addStart)
            {
                AnimationCurve leftCurve = originalCurves[lastBeforeStart];
                float leftTime = original[lastBeforeStart].Key;
                AnimationCurve curve;
                if (firstAfterStart != -1)
                {
                    float rightTime = original[firstAfterStart].Key;
                    float u0 = (start - leftTime) / (rightTime - leftTime);
                    float u1 = 1f;
                    if (addEnd && firstAfterStart == firstAfterEnd) // Both edges fall inside the same segment
                        u1 = (end - leftTime) / (rightTime - leftTime);
                    curve = GetSubCurve(leftCurve, u0, u1);
                }
                else
                    curve = new AnimationCurve(leftCurve.keys);
                startKeyframe = new Keyframe(startValue, interpolable, curve);
            }

            Keyframe endKeyframe = null;
            if (addEnd)
            {
                AnimationCurve baseCurve = lastBeforeEnd != -1 ? originalCurves[lastBeforeEnd] : originalCurves[firstAfterEnd];
                endKeyframe = new Keyframe(endValue, interpolable, new AnimationCurve(baseCurve.keys));

                // The keyframe right before the end edge now leads to the edge keyframe: cut its curve at the edge.
                if (lastBeforeEnd != -1 && original[lastBeforeEnd].Key >= start - _trimTimeEpsilon)
                {
                    float leftTime = original[lastBeforeEnd].Key;
                    float rightTime = original[firstAfterEnd].Key;
                    float u1 = (end - leftTime) / (rightTime - leftTime);
                    original[lastBeforeEnd].Value.curve = GetSubCurve(originalCurves[lastBeforeEnd], 0f, u1);
                }
            }

            List<KeyValuePair<float, Keyframe>> kept = new List<KeyValuePair<float, Keyframe>>(count + 2);
            if (startKeyframe != null)
            {
                kept.Add(new KeyValuePair<float, Keyframe>(start, startKeyframe));
                added++;
            }
            foreach (KeyValuePair<float, Keyframe> pair in original)
            {
                if (pair.Key >= start - _trimTimeEpsilon && pair.Key <= end + _trimTimeEpsilon)
                    kept.Add(pair);
                else
                    removed++;
            }
            if (endKeyframe != null)
            {
                kept.Add(new KeyValuePair<float, Keyframe>(end, endKeyframe));
                added++;
            }

            interpolable.keyframes.Clear();
            foreach (KeyValuePair<float, Keyframe> pair in kept)
            {
                float time = pair.Key;
                if (moveToStart)
                    time = Mathf.Max(0f, time - start);
                if (interpolable.keyframes.ContainsKey(time))
                    continue;
                interpolable.keyframes.Add(time, pair.Value);
            }
        }

        private static bool HasKeyframeNear(IList<float> times, float time)
        {
            for (int i = 0; i < times.Count; i++)
                if (Mathf.Abs(times[i] - time) <= _trimTimeEpsilon)
                    return true;
            return false;
        }

        private void ApplyPlaybackTime(float time)
        {
            _playbackTime = time;
            _startTime = Time.time - time;
            bool wasPlaying = _isPlaying;
            _isPlaying = true;
            UpdateCursor();
            Interpolate(true);
            Interpolate(false);
            _isPlaying = wasPlaying;
        }

        /// <summary>
        /// Plays the timeline at the given time and reads the resulting values, same as "Add keyframe at cursor" does.
        /// Falls back to a value computed from the keyframes for disabled interpolables or if reading fails.
        /// </summary>
        private Dictionary<Interpolable, object> SampleInterpolableValues(float time, List<Interpolable> interpolables)
        {
            Dictionary<Interpolable, object> result = new Dictionary<Interpolable, object>();
            if (interpolables.Count == 0)
                return result;

            ApplyPlaybackTime(time);
            foreach (Interpolable interpolable in interpolables)
            {
                object value = null;
                if (interpolable.enabled)
                {
                    try
                    {
                        value = interpolable.GetValue();
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"Trim: couldn't read the value of \"{interpolable}\" at {time}, estimating it from keyframes instead\n{e}");
                    }
                }
                if (value == null)
                    value = EstimateValueFromKeyframes(interpolable, time);
                if (value != null)
                    result[interpolable] = value;
            }
            return result;
        }

        private static object EstimateValueFromKeyframes(Interpolable interpolable, float time)
        {
            KeyValuePair<float, Keyframe> left = default;
            KeyValuePair<float, Keyframe> right = default;
            foreach (KeyValuePair<float, Keyframe> pair in interpolable.keyframes)
            {
                if (pair.Key <= time)
                    left = pair;
                else
                {
                    right = pair;
                    break;
                }
            }
            if (left.Value != null && right.Value != null)
            {
                float factor = left.Value.curve.Evaluate((time - left.Key) / (right.Key - left.Key));
                if (TryLerpValue(left.Value.value, right.Value.value, factor, out object lerped))
                    return lerped;
                return factor >= 1f ? right.Value.value : left.Value.value;
            }
            if (left.Value != null)
                return left.Value.value;
            return right.Value?.value;
        }

        private static bool TryLerpValue(object a, object b, float t, out object result)
        {
            result = null;
            if (a == null || b == null || a.GetType() != b.GetType())
                return false;
            switch (a)
            {
                case float f:
                    result = Mathf.LerpUnclamped(f, (float)b, t);
                    return true;
                case Vector2 v2:
                    result = Vector2.LerpUnclamped(v2, (Vector2)b, t);
                    return true;
                case Vector3 v3:
                    result = Vector3.LerpUnclamped(v3, (Vector3)b, t);
                    return true;
                case Vector4 v4:
                    result = Vector4.LerpUnclamped(v4, (Vector4)b, t);
                    return true;
                case Quaternion q:
                    result = Quaternion.SlerpUnclamped(q, (Quaternion)b, t);
                    return true;
                case Color c:
                    result = Color.LerpUnclamped(c, (Color)b, t);
                    return true;
            }
            return false;
        }
        #endregion

        #region Trim - Curve splitting
        /// <summary>
        /// Returns the part of a 0-1 interpolation curve between u0 and u1, renormalized to 0-1 in both time and value.
        /// A Hermite segment split at any point is still a Hermite segment, so the result is exact.
        /// </summary>
        private static AnimationCurve GetSubCurve(AnimationCurve curve, float u0, float u1)
        {
            UnityEngine.Keyframe[] keys = curve.keys;
            float du = u1 - u0;
            if (keys.Length == 0 || du < 1e-6f)
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            if (u0 <= 1e-6f && u1 >= 1f - 1e-6f)
                return new AnimationCurve(keys);

            float c0 = curve.Evaluate(u0);
            float c1 = curve.Evaluate(u1);
            float dc = c1 - c0;
            // The sub-range is flat (e.g. inside a stepped segment): both edge values are equal, any curve works.
            if (Mathf.Abs(dc) < 1e-6f)
                return AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float tangentScale = du / dc;
            List<UnityEngine.Keyframe> result = new List<UnityEngine.Keyframe>(keys.Length + 2);

            float outTangent = GetCurveSlope(keys, u0, true) * tangentScale;
            result.Add(new UnityEngine.Keyframe(0f, 0f, outTangent, outTangent));
            foreach (UnityEngine.Keyframe key in keys)
            {
                if (key.time <= u0 + _curveTimeTolerance || key.time >= u1 - _curveTimeTolerance)
                    continue;
                result.Add(new UnityEngine.Keyframe((key.time - u0) / du, (key.value - c0) / dc, key.inTangent * tangentScale, key.outTangent * tangentScale));
            }
            float inTangent = GetCurveSlope(keys, u1, false) * tangentScale;
            result.Add(new UnityEngine.Keyframe(1f, 1f, inTangent, inTangent));

            return new AnimationCurve(result.ToArray());
        }

        private const float _curveTimeTolerance = 1e-6f;

        /// <summary>
        /// Slope of the curve at u, taken from the right (start of the following segment) or from the left.
        /// Inside a stepped segment (infinite tangent) this returns infinity so the split part stays stepped.
        /// </summary>
        private static float GetCurveSlope(UnityEngine.Keyframe[] keys, float u, bool fromRight)
        {
            int n = keys.Length;
            if (n < 2)
                return 0f;

            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(keys[i].time - u) > _curveTimeTolerance)
                    continue;
                if (fromRight)
                {
                    if (i == n - 1)
                        return 0f;
                    return IsSteppedSegment(keys[i], keys[i + 1]) ? float.PositiveInfinity : keys[i].outTangent;
                }
                if (i == 0)
                    return 0f;
                return IsSteppedSegment(keys[i - 1], keys[i]) ? float.PositiveInfinity : keys[i].inTangent;
            }

            if (u <= keys[0].time || u >= keys[n - 1].time)
                return 0f;

            for (int i = 0; i < n - 1; i++)
            {
                UnityEngine.Keyframe k0 = keys[i];
                UnityEngine.Keyframe k1 = keys[i + 1];
                if (u <= k0.time || u >= k1.time)
                    continue;
                if (IsSteppedSegment(k0, k1))
                    return float.PositiveInfinity;
                float dt = k1.time - k0.time;
                float s = (u - k0.time) / dt;
                float m0 = k0.outTangent * dt;
                float m1 = k1.inTangent * dt;
                float d = (6f * s * s - 6f * s) * k0.value
                        + (3f * s * s - 4f * s + 1f) * m0
                        + (-6f * s * s + 6f * s) * k1.value
                        + (3f * s * s - 2f * s) * m1;
                return d / dt;
            }
            return 0f;
        }

        private static bool IsSteppedSegment(UnityEngine.Keyframe k0, UnityEngine.Keyframe k1)
        {
            return float.IsInfinity(k0.outTangent) || float.IsInfinity(k1.inTangent);
        }
        #endregion
    }
}
