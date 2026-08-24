using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UiButton = UnityEngine.UI.Button;
using UiDropdown = UnityEngine.UI.Dropdown;
using UiInputField = UnityEngine.UI.InputField;
using UiScrollbar = UnityEngine.UI.Scrollbar;
using UiSelectable = UnityEngine.UI.Selectable;
using UiSlider = UnityEngine.UI.Slider;
using UiText = UnityEngine.UI.Text;
using UiToggle = UnityEngine.UI.Toggle;

namespace ErenshorSkills.Testing
{
    /// <summary>
    /// Test-mode-only semantic bridge for Erenshor's uGUI controls. Requests
    /// are processed on Unity's main thread so MCP clients never need screen
    /// coordinates or synthetic mouse input.
    /// </summary>
    internal sealed class UiAutomationBridge : MonoBehaviour
    {
        private const string RequestFileName = "ui-automation-request.json";
        private const string ResponseDirectoryName = "UiAutomationResponses";
        private const int MaximumRequestLength = 16384;
        private const int MaximumControlsPerPage = 200;
        private const int MaximumValueLength = 4096;
        private float _nextPoll;

        [Serializable]
        public sealed class UiRequest
        {
            public string requestId = "";
            public string operation = "";
            public string controlId = "";
            public string value = "";
            public bool includeInactive = false;
            public int offset = 0;
            public int limit = 100;
            public bool allowDangerous = false;
        }

        [Serializable]
        public sealed class UiResponse
        {
            public string requestId;
            public bool success;
            public string operation;
            public string message;
            public string scene;
            public bool truncated;
            public int totalControls;
            public int offset;
            public UiControlDescriptor control;
            public List<UiControlDescriptor> controls =
                new List<UiControlDescriptor>();
        }

        [Serializable]
        public sealed class UiControlDescriptor
        {
            public string id;
            public string kind;
            public string name;
            public string label;
            public string scene;
            public string path;
            public string componentType;
            public bool active;
            public bool interactable;
            public bool dangerous;
            public string value;
            public string selectedOption;
            public float minimum;
            public float maximum;
            public bool wholeNumbers;
            public List<string> options = new List<string>();
        }

        private void Update()
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value != true) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            PollRequest();
        }

        private static void PollRequest()
        {
            string path = Path.Combine(RuntimePaths.DataDirectory,
                RequestFileName);
            if (!File.Exists(path)) return;

            string payload = null;
            try
            {
                payload = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "UI automation request read failed: " + ex);
                return;
            }
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                // Never risk executing the same semantic action twice.
                SkillsPlugin.Log.LogWarning(
                    "UI automation request cleanup failed; request was not executed: " +
                    ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(payload) ||
                payload.Length > MaximumRequestLength)
            {
                SkillsPlugin.Log.LogWarning(
                    "Ignored invalid UI automation request length.");
                return;
            }

            UiRequest request = null;
            try { request = JsonUtility.FromJson<UiRequest>(payload); }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    "Ignored malformed UI automation request: " + ex.Message);
            }
            if (request == null || !IsSafeRequestId(request.requestId))
            {
                SkillsPlugin.Log.LogWarning(
                    "Ignored UI automation request with an invalid request ID.");
                return;
            }

            UiResponse response = Execute(request);
            WriteResponse(response);
        }

        private static UiResponse Execute(UiRequest request)
        {
            var response = new UiResponse
            {
                requestId = request.requestId,
                operation = request.operation ?? "",
                scene = SceneManager.GetActiveScene().name
            };

            try
            {
                string operation = (request.operation ?? "").Trim()
                    .ToLowerInvariant();
                switch (operation)
                {
                    case "list_controls":
                        ListControls(request.includeInactive, request.offset,
                            request.limit, response);
                        break;
                    case "activate":
                        ActivateControl(request, response);
                        break;
                    case "set_value":
                        SetControlValue(request, response);
                        break;
                    default:
                        response.success = false;
                        response.message =
                            "Unknown UI automation operation.";
                        break;
                }
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = ex.GetType().Name + ": " + ex.Message;
                SkillsPlugin.Log.LogError(
                    "UI automation execution failed: " + ex);
            }
            return response;
        }

        private static void ListControls(bool includeInactive, int offset,
            int limit, UiResponse response)
        {
            List<UiSelectable> selectables = FindControls(includeInactive)
                .OrderBy(control => SceneName(control), StringComparer.Ordinal)
                .ThenBy(control => TransformPath(control.transform),
                    StringComparer.Ordinal)
                .ThenBy(control => ComponentIndex(control))
                .ToList();

            int safeOffset = Mathf.Clamp(offset, 0, selectables.Count);
            int safeLimit = Mathf.Clamp(limit, 1, MaximumControlsPerPage);
            response.totalControls = selectables.Count;
            response.offset = safeOffset;
            response.truncated =
                safeOffset + safeLimit < selectables.Count;
            foreach (UiSelectable selectable in selectables
                .Skip(safeOffset).Take(safeLimit))
                response.controls.Add(Describe(selectable));
            response.success = true;
            response.message =
                $"Found {selectables.Count} Unity UI control(s); returned " +
                $"{response.controls.Count} from offset {safeOffset}.";
        }

        private static void ActivateControl(UiRequest request,
            UiResponse response)
        {
            UiSelectable selectable = FindControl(request.controlId);
            if (selectable == null)
            {
                response.success = false;
                response.message =
                    "Control ID was not found. List controls again after every UI transition.";
                return;
            }

            UiControlDescriptor descriptor = Describe(selectable);
            response.control = descriptor;
            if (!descriptor.active || !descriptor.interactable)
            {
                response.success = false;
                response.message = "Control is not active and interactable.";
                return;
            }
            if (!(selectable is UiButton button))
            {
                response.success = false;
                response.message =
                    $"Control kind '{descriptor.kind}' cannot be activated; use set_ui_control for value controls.";
                return;
            }
            if (descriptor.dangerous && !request.allowDangerous)
            {
                response.success = false;
                response.message =
                    "Button looks destructive or session-ending. Retry with allowDangerous=true only after explicit approval.";
                return;
            }

            string label = string.IsNullOrEmpty(descriptor.label)
                ? descriptor.name : descriptor.label;
            SkillsPlugin.Log.LogInfo(
                $"[UI Automation] Activating button '{label}' ({descriptor.path}).");
            button.onClick.Invoke();
            response.success = true;
            response.message = $"Activated button '{label}'.";
            response.scene = SceneManager.GetActiveScene().name;
        }

        private static void SetControlValue(UiRequest request,
            UiResponse response)
        {
            UiSelectable selectable = FindControl(request.controlId);
            if (selectable == null)
            {
                response.success = false;
                response.message =
                    "Control ID was not found. List controls again after every UI transition.";
                return;
            }

            UiControlDescriptor before = Describe(selectable);
            response.control = before;
            if (!before.active || !before.interactable)
            {
                response.success = false;
                response.message = "Control is not active and interactable.";
                return;
            }

            string value = request.value ?? "";
            if (value.Length > MaximumValueLength)
            {
                response.success = false;
                response.message =
                    $"Control value exceeds {MaximumValueLength} characters.";
                return;
            }

            string detail;
            if (selectable is UiToggle toggle)
            {
                if (!bool.TryParse(value, out bool parsed))
                    throw new ArgumentException(
                        "Toggle value must be true or false.");
                toggle.isOn = parsed;
                detail = parsed ? "true" : "false";
            }
            else if (selectable is UiSlider slider)
            {
                float parsed = ParseFloat(value, "slider");
                if (parsed < slider.minValue || parsed > slider.maxValue)
                    throw new ArgumentOutOfRangeException(nameof(request.value),
                        $"Slider value must be between {slider.minValue} and {slider.maxValue}.");
                slider.value = parsed;
                detail = slider.value.ToString("G9",
                    CultureInfo.InvariantCulture);
            }
            else if (selectable is UiScrollbar scrollbar)
            {
                float parsed = ParseFloat(value, "scrollbar");
                if (parsed < 0f || parsed > 1f)
                    throw new ArgumentOutOfRangeException(nameof(request.value),
                        "Scrollbar value must be between 0 and 1.");
                scrollbar.value = parsed;
                detail = scrollbar.value.ToString("G9",
                    CultureInfo.InvariantCulture);
            }
            else if (selectable is TMP_Dropdown tmpDropdown)
            {
                int index = ResolveDropdownIndex(value,
                    tmpDropdown.options.Select(option => option.text).ToList());
                tmpDropdown.value = index;
                tmpDropdown.RefreshShownValue();
                detail = tmpDropdown.options[index].text;
            }
            else if (selectable is UiDropdown dropdown)
            {
                int index = ResolveDropdownIndex(value,
                    dropdown.options.Select(option => option.text).ToList());
                dropdown.value = index;
                dropdown.RefreshShownValue();
                detail = dropdown.options[index].text;
            }
            else if (selectable is TMP_InputField tmpInput)
            {
                tmpInput.text = value;
                detail = tmpInput.text;
            }
            else if (selectable is UiInputField input)
            {
                input.text = value;
                detail = input.text;
            }
            else
            {
                response.success = false;
                response.message =
                    $"Control kind '{before.kind}' does not expose a settable value.";
                return;
            }

            response.control = Describe(selectable);
            response.success = true;
            response.message =
                $"Set {before.kind} '{DisplayName(before)}' to '{Clean(detail, 240)}'.";
            SkillsPlugin.Log.LogInfo(
                "[UI Automation] " + response.message);
        }

        private static IEnumerable<UiSelectable> FindControls(
            bool includeInactive)
        {
            return Resources.FindObjectsOfTypeAll<UiSelectable>()
                .Where(control => control != null &&
                    control.gameObject != null &&
                    control.gameObject.scene.IsValid() &&
                    (includeInactive || control.gameObject.activeInHierarchy));
        }

        private static UiSelectable FindControl(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return FindControls(true).FirstOrDefault(control =>
                string.Equals(ControlId(control), id,
                    StringComparison.Ordinal));
        }

        private static UiControlDescriptor Describe(UiSelectable selectable)
        {
            string kind = ControlKind(selectable);
            var descriptor = new UiControlDescriptor
            {
                id = ControlId(selectable),
                kind = kind,
                name = Clean(selectable.gameObject.name, 240),
                label = FindLabel(selectable),
                scene = SceneName(selectable),
                path = TransformPath(selectable.transform),
                componentType = selectable.GetType().FullName,
                active = selectable.gameObject.activeInHierarchy &&
                    selectable.isActiveAndEnabled,
                interactable = selectable.IsInteractable(),
                value = "",
                selectedOption = ""
            };
            descriptor.dangerous = kind == "button" &&
                IsDangerous(descriptor.name + " " + descriptor.label + " " +
                    descriptor.path);

            if (selectable is UiToggle toggle)
                descriptor.value = toggle.isOn ? "true" : "false";
            else if (selectable is UiSlider slider)
            {
                descriptor.value = slider.value.ToString("G9",
                    CultureInfo.InvariantCulture);
                descriptor.minimum = slider.minValue;
                descriptor.maximum = slider.maxValue;
                descriptor.wholeNumbers = slider.wholeNumbers;
            }
            else if (selectable is UiScrollbar scrollbar)
            {
                descriptor.value = scrollbar.value.ToString("G9",
                    CultureInfo.InvariantCulture);
                descriptor.minimum = 0f;
                descriptor.maximum = 1f;
            }
            else if (selectable is TMP_Dropdown tmpDropdown)
            {
                descriptor.value = tmpDropdown.value.ToString(
                    CultureInfo.InvariantCulture);
                descriptor.options = tmpDropdown.options
                    .Select(option => Clean(option.text, 240)).ToList();
                if (tmpDropdown.value >= 0 &&
                    tmpDropdown.value < descriptor.options.Count)
                    descriptor.selectedOption =
                        descriptor.options[tmpDropdown.value];
            }
            else if (selectable is UiDropdown dropdown)
            {
                descriptor.value = dropdown.value.ToString(
                    CultureInfo.InvariantCulture);
                descriptor.options = dropdown.options
                    .Select(option => Clean(option.text, 240)).ToList();
                if (dropdown.value >= 0 &&
                    dropdown.value < descriptor.options.Count)
                    descriptor.selectedOption =
                        descriptor.options[dropdown.value];
            }
            else if (selectable is TMP_InputField tmpInput)
                descriptor.value = Clean(tmpInput.text, MaximumValueLength);
            else if (selectable is UiInputField input)
                descriptor.value = Clean(input.text, MaximumValueLength);

            return descriptor;
        }

        private static string ControlKind(UiSelectable selectable)
        {
            if (selectable is UiButton) return "button";
            if (selectable is UiToggle) return "toggle";
            if (selectable is UiSlider) return "slider";
            if (selectable is UiScrollbar) return "scrollbar";
            if (selectable is TMP_Dropdown) return "tmp_dropdown";
            if (selectable is UiDropdown) return "dropdown";
            if (selectable is TMP_InputField) return "tmp_input";
            if (selectable is UiInputField) return "input";
            return "selectable";
        }

        private static string ControlId(UiSelectable selectable)
        {
            string key = SceneName(selectable) + "|" +
                TransformPath(selectable.transform) + "|" +
                ComponentIndex(selectable) + "|" +
                selectable.GetType().FullName;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static int ComponentIndex(UiSelectable selectable)
        {
            UiSelectable[] components =
                selectable.gameObject.GetComponents<UiSelectable>();
            for (int index = 0; index < components.Length; index++)
                if (ReferenceEquals(components[index], selectable))
                    return index;
            return 0;
        }

        private static string SceneName(UiSelectable selectable)
        {
            Scene scene = selectable.gameObject.scene;
            return scene.IsValid() ? scene.name : "";
        }

        private static string TransformPath(Transform transform)
        {
            var segments = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                segments.Push(Clean(current.name, 160) + "[" +
                    current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            return string.Join("/", segments.ToArray());
        }

        private static string FindLabel(UiSelectable selectable)
        {
            TMP_Text[] tmp = selectable.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in tmp)
            {
                string value = Clean(text?.text, 240);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            UiText[] legacy =
                selectable.GetComponentsInChildren<UiText>(true);
            foreach (UiText text in legacy)
            {
                string value = Clean(text?.text, 240);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private static string DisplayName(UiControlDescriptor descriptor)
        {
            return string.IsNullOrWhiteSpace(descriptor.label)
                ? descriptor.name : descriptor.label;
        }

        private static float ParseFloat(string value, string kind)
        {
            if (!float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed))
                throw new ArgumentException(
                    $"{kind} value must be a decimal number.");
            return parsed;
        }

        private static int ResolveDropdownIndex(string value,
            List<string> options)
        {
            if (int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int index))
            {
                if (index >= 0 && index < options.Count) return index;
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Dropdown index must be between 0 and {options.Count - 1}.");
            }

            index = options.FindIndex(option =>
                string.Equals(option, value,
                    StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
            throw new ArgumentException(
                "Dropdown value must be a valid zero-based index or exact option label.");
        }

        private static bool IsDangerous(string text)
        {
            string value = (text ?? "").ToLowerInvariant();
            string[] terms =
            {
                "delete", "erase", "overwrite", "new game",
                "quit", "exit game", "exit desktop", "reset save",
                "remove character"
            };
            return terms.Any(term => value.Contains(term));
        }

        private static bool IsSafeRequestId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' &&
                    character != '_') return false;
            }
            return true;
        }

        private static string Clean(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string cleaned = value.Replace('\r', ' ').Replace('\n', ' ')
                .Replace('\t', ' ').Trim();
            while (cleaned.Contains("  "))
                cleaned = cleaned.Replace("  ", " ");
            return cleaned.Length <= maximum
                ? cleaned : cleaned.Substring(0, maximum);
        }

        private static string SerializeResponse(UiResponse response)
        {
            var output = new StringBuilder();
            output.Append('{');
            AppendJsonProperty(output, "requestId", response.requestId);
            output.Append(',');
            AppendJsonProperty(output, "success", response.success);
            output.Append(',');
            AppendJsonProperty(output, "operation", response.operation);
            output.Append(',');
            AppendJsonProperty(output, "message", response.message);
            output.Append(',');
            AppendJsonProperty(output, "scene", response.scene);
            output.Append(',');
            AppendJsonProperty(output, "truncated", response.truncated);
            output.Append(",\"totalControls\":")
                .Append(response.totalControls);
            output.Append(",\"offset\":").Append(response.offset);
            output.Append(",\"control\":");
            if (response.control == null) output.Append("null");
            else AppendJsonControl(output, response.control);
            output.Append(",\"controls\":[");
            for (int index = 0; index < response.controls.Count; index++)
            {
                if (index > 0) output.Append(',');
                AppendJsonControl(output, response.controls[index]);
            }
            output.Append("]}");
            return output.ToString();
        }

        private static void AppendJsonControl(StringBuilder output,
            UiControlDescriptor control)
        {
            output.Append('{');
            AppendJsonProperty(output, "id", control.id);
            output.Append(',');
            AppendJsonProperty(output, "kind", control.kind);
            output.Append(',');
            AppendJsonProperty(output, "name", control.name);
            output.Append(',');
            AppendJsonProperty(output, "label", control.label);
            output.Append(',');
            AppendJsonProperty(output, "scene", control.scene);
            output.Append(',');
            AppendJsonProperty(output, "path", control.path);
            output.Append(',');
            AppendJsonProperty(output, "componentType",
                control.componentType);
            output.Append(',');
            AppendJsonProperty(output, "active", control.active);
            output.Append(',');
            AppendJsonProperty(output, "interactable",
                control.interactable);
            output.Append(',');
            AppendJsonProperty(output, "dangerous", control.dangerous);
            output.Append(',');
            AppendJsonProperty(output, "value", control.value);
            output.Append(',');
            AppendJsonProperty(output, "selectedOption",
                control.selectedOption);
            output.Append(",\"minimum\":")
                .Append(control.minimum.ToString("G9",
                    CultureInfo.InvariantCulture));
            output.Append(",\"maximum\":")
                .Append(control.maximum.ToString("G9",
                    CultureInfo.InvariantCulture));
            output.Append(',');
            AppendJsonProperty(output, "wholeNumbers",
                control.wholeNumbers);
            output.Append(",\"options\":[");
            for (int index = 0; index < control.options.Count; index++)
            {
                if (index > 0) output.Append(',');
                AppendJsonString(output, control.options[index]);
            }
            output.Append("]}");
        }

        private static void AppendJsonProperty(StringBuilder output,
            string name, string value)
        {
            AppendJsonString(output, name);
            output.Append(':');
            AppendJsonString(output, value);
        }

        private static void AppendJsonProperty(StringBuilder output,
            string name, bool value)
        {
            AppendJsonString(output, name);
            output.Append(':').Append(value ? "true" : "false");
        }

        private static void AppendJsonString(StringBuilder output,
            string value)
        {
            output.Append('"')
                .Append(SafeFileIO.EscapeJson(value ?? ""))
                .Append('"');
        }

        private static void WriteResponse(UiResponse response)
        {
            try
            {
                string directory = Path.Combine(RuntimePaths.DataDirectory,
                    ResponseDirectoryName);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory,
                    response.requestId + ".json");
                string json = SerializeResponse(response);
                if (!SafeFileIO.TryWriteAllTextAtomic(path, json,
                    out string error))
                    throw new IOException(error);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "UI automation response write failed: " + ex);
            }
        }
    }
}
