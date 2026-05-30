const api = window["cs2/api"];
const ui = window["cs2/ui"];
const React = window.React;
const h = React.createElement;

const group = "betatestdrivingmod";
const icon = "coui://ui-mods/images/beta-test-driving-mod.svg";
let lastTopLeftToggle = 0;

const panelVisible = api.bindValue(group, "PanelVisible", false);
const isDriving = api.bindValue(group, "IsDriving", false);
const statusText = api.bindValue(group, "StatusText", "Select or look near a car, then press V.");
const possessedName = api.bindValue(group, "PossessedName", "");
const controlStatus = api.bindValue(group, "ControlStatus", "Direct control ready");
const speedMph = api.bindValue(group, "SpeedMph", 0);
const braking = api.bindValue(group, "Braking", false);
const reverseReady = api.bindValue(group, "ReverseReady", false);
const inputThrottle = api.bindValue(group, "InputThrottle", 0);
const inputBrake = api.bindValue(group, "InputBrake", 0);
const inputSteering = api.bindValue(group, "InputSteering", 0);
const inputFresh = api.bindValue(group, "InputFresh", true);
const inputAgeSeconds = api.bindValue(group, "InputAgeSeconds", 0);
const roadIntentAssist = api.bindValue(group, "RoadIntentAssist", true);
const roadHeightAssist = api.bindValue(group, "RoadHeightAssist", true);
const freezeVanillaNavigation = api.bindValue(group, "FreezeVanillaNavigation", true);
const chaseCameraEnabled = api.bindValue(group, "ChaseCameraEnabled", true);
const chaseCameraStatus = api.bindValue(group, "ChaseCameraStatus", "Chase camera ready");
const policeChaseEnabled = api.bindValue(group, "PoliceChaseEnabled", false);
const policeChaseActive = api.bindValue(group, "PoliceChaseActive", false);
const policeChaseStatus = api.bindValue(group, "PoliceChaseStatus", "Police chase armed");
const policeChaseUnits = api.bindValue(group, "PoliceChaseUnits", 0);
const redLightViolations = api.bindValue(group, "RedLightViolations", 0);
const targetSpeedMph = api.bindValue(group, "TargetSpeedMph", 42);
const reverseSpeedMph = api.bindValue(group, "ReverseSpeedMph", 9);
const accelerationMps2 = api.bindValue(group, "AccelerationMps2", 19);
const brakeMps2 = api.bindValue(group, "BrakeMps2", 42);
const coastMps2 = api.bindValue(group, "CoastMps2", 12);
const reverseAccelerationMps2 = api.bindValue(group, "ReverseAccelerationMps2", 12);
const maxTurnDegPerSecond = api.bindValue(group, "MaxTurnDegPerSecond", 148);
const lowSpeedTurnBoost = api.bindValue(group, "LowSpeedTurnBoost", 0.58);
const roadHeightStickiness = api.bindValue(group, "RoadHeightStickiness", 0.45);
const chaseCameraDistance = api.bindValue(group, "ChaseCameraDistance", 10.5);
const chaseCameraHeight = api.bindValue(group, "ChaseCameraHeight", 3.25);
const chaseCameraLookAhead = api.bindValue(group, "ChaseCameraLookAhead", 12);

const panel = {
    position: "absolute",
    top: "88rem",
    left: "86rem",
    width: "430rem",
    maxHeight: "760rem",
    padding: "0",
    overflow: "hidden",
    color: "rgba(245, 248, 250, 0.96)",
    backgroundColor: "rgba(9, 14, 18, 0.88)",
    border: "1rem solid rgba(255, 255, 255, 0.22)",
    borderRadius: "8rem",
    boxShadow: "0 18rem 46rem rgba(0, 0, 0, 0.46)",
    backdropFilter: "blur(10rem)",
    pointerEvents: "auto",
    fontSize: "13rem"
};

const header = {
    padding: "12rem 13rem",
    backgroundColor: "rgba(25, 34, 39, 0.82)",
    borderBottom: "1rem solid rgba(255, 255, 255, 0.14)"
};

const content = {
    padding: "12rem",
    maxHeight: "650rem",
    overflowY: "auto"
};

const button = {
    minHeight: "30rem",
    padding: "5rem 11rem",
    color: "white",
    backgroundColor: "rgba(255, 255, 255, 0.105)",
    border: "1rem solid rgba(255, 255, 255, 0.22)",
    borderRadius: "6rem",
    textAlign: "center",
    whiteSpace: "nowrap"
};

const primaryButton = Object.assign({}, button, {
    backgroundColor: "rgba(32, 164, 122, 0.92)",
    border: "1rem solid rgba(119, 242, 202, 0.62)",
    color: "rgba(3, 18, 14, 0.95)",
    fontWeight: "700"
});

const dangerButton = Object.assign({}, button, {
    backgroundColor: "rgba(208, 83, 73, 0.92)",
    border: "1rem solid rgba(255, 176, 166, 0.54)",
    fontWeight: "700"
});

const row = {
    gap: "8rem"
};

function trigger(name, ...args) {
    api.trigger(group, name, ...args);
}

function togglePanelFromButton() {
    const now = Date.now();
    if (now - lastTopLeftToggle < 180) {
        return;
    }

    lastTopLeftToggle = now;
    trigger("TogglePanel");
}

function clampNumber(value, fallback) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
}

function decimalsForStep(step) {
    const text = `${step || 1}`;
    const dot = text.indexOf(".");
    return dot >= 0 ? text.length - dot - 1 : 0;
}

function formatNumberInput(value, step) {
    if (!Number.isFinite(value)) {
        return "";
    }

    const decimals = decimalsForStep(step);
    if (decimals <= 0) {
        return Math.round(value).toString();
    }

    return value.toFixed(decimals).replace(/\.?0+$/, "");
}

function commandButton(label, onClick, style) {
    return h("button", {
        style: Object.assign({}, button, style || null),
        onClick
    }, label);
}

function Pill({ label, tone }) {
    const tones = {
        green: ["rgba(62, 216, 156, 0.18)", "rgba(97, 255, 198, 0.55)"],
        amber: ["rgba(230, 171, 66, 0.18)", "rgba(255, 213, 123, 0.55)"],
        red: ["rgba(227, 86, 77, 0.18)", "rgba(255, 152, 143, 0.55)"],
        blue: ["rgba(70, 171, 232, 0.17)", "rgba(134, 218, 255, 0.52)"]
    };
    const pair = tones[tone] || tones.blue;
    return h("span", {
        style: {
            minHeight: "22rem",
            padding: "2rem 8rem",
            borderRadius: "999rem",
            backgroundColor: pair[0],
            border: `1rem solid ${pair[1]}`,
            color: "rgba(246, 252, 255, 0.94)",
            fontSize: "12rem"
        }
    }, label);
}

function TabButton({ id, selected, setTab, children }) {
    return h("button", {
        style: {
            flex: 1,
            minHeight: "30rem",
            borderRadius: "6rem",
            border: selected ? "1rem solid rgba(114, 229, 188, 0.68)" : "1rem solid rgba(255, 255, 255, 0.1)",
            backgroundColor: selected ? "rgba(43, 138, 111, 0.44)" : "rgba(255, 255, 255, 0.055)",
            color: selected ? "white" : "rgba(230, 236, 240, 0.78)",
            fontWeight: selected ? "700" : "500"
        },
        onClick: () => setTab(id)
    }, children);
}

function ToggleRow({ binding, action, label, hint }) {
    const value = api.useValue(binding);
    return h("div", {
        style: Object.assign({}, row, {
            justifyContent: "space-between",
            alignItems: "center",
            minHeight: "34rem",
            padding: "7rem 0",
            borderTop: "1rem solid rgba(255, 255, 255, 0.08)"
        })
    },
        h("div", {
            style: {
                minWidth: 0,
                flex: 1,
                flexDirection: "column",
                gap: "2rem",
                lineHeight: "1.18"
            }
        },
            h("div", { style: { fontWeight: "650" } }, label),
            hint ? h("div", { style: { opacity: 0.66, fontSize: "12rem", lineHeight: "1.18" } }, hint) : null
        ),
        h("input", {
            type: "checkbox",
            checked: !!value,
            onChange: event => trigger(action, !!event.target.checked),
            style: { width: "20rem", height: "20rem", flex: "0 0 auto" }
        })
    );
}

function SliderRow({ binding, action, label, step, format, unit }) {
    const value = clampNumber(api.useValue(binding), 0);
    const display = format ? format(value) : Math.round(value).toString();
    const [draft, setDraft] = React.useState(formatNumberInput(value, step));
    const [editing, setEditing] = React.useState(false);

    React.useEffect(() => {
        if (!editing) {
            setDraft(formatNumberInput(value, step));
        }
    }, [editing, step, value]);

    const commit = () => {
        const trimmed = draft.trim();
        if (trimmed === "" || trimmed === "-" || trimmed === "." || trimmed === "-.") {
            setDraft(formatNumberInput(value, step));
            setEditing(false);
            return;
        }

        const parsed = Number(trimmed);
        if (!Number.isFinite(parsed)) {
            setDraft(formatNumberInput(value, step));
            setEditing(false);
            return;
        }

        setDraft(formatNumberInput(parsed, step));
        setEditing(false);
        trigger(action, parsed);
    };

    const resetDraft = () => {
        setDraft(formatNumberInput(value, step));
        setEditing(false);
    };

    return h("div", { style: { padding: "8rem 0", borderTop: "1rem solid rgba(255, 255, 255, 0.08)" } },
        h("div", { style: Object.assign({}, row, { justifyContent: "space-between", marginBottom: "5rem" }) },
            h("span", { style: { fontWeight: "650" } }, label),
            h("span", { style: { opacity: 0.82 } }, `${display}${unit || ""}`)
        ),
        h("input", {
            type: "text",
            value: draft,
            onFocus: () => setEditing(true),
            onInput: event => setDraft(event.target.value),
            onChange: event => setDraft(event.target.value),
            onBlur: commit,
            onKeyDown: event => {
                if (event.key === "Enter") {
                    commit();
                    event.target.blur();
                } else if (event.key === "Escape") {
                    resetDraft();
                    event.target.blur();
                }
            },
            style: {
                width: "100%",
                minHeight: "30rem",
                padding: "4rem 8rem",
                color: "rgba(245, 248, 250, 0.96)",
                backgroundColor: editing ? "rgba(255, 255, 255, 0.16)" : "rgba(255, 255, 255, 0.08)",
                border: "1rem solid rgba(255, 255, 255, 0.22)",
                borderRadius: "6rem",
                textAlign: "right"
            }
        })
    );
}

function Meter({ label, value, center }) {
    const raw = clampNumber(value, 0);
    const normalized = center ? (raw + 1) / 2 : raw;
    const width = `${Math.max(0, Math.min(1, normalized)) * 100}%`;
    return h("div", { style: { flex: 1, minWidth: 0 } },
        h("div", { style: { opacity: 0.72, marginBottom: "4rem", fontSize: "11rem" } }, label),
        h("div", {
            style: {
                height: "7rem",
                overflow: "hidden",
                borderRadius: "999rem",
                backgroundColor: "rgba(255, 255, 255, 0.12)"
            }
        },
            h("div", {
                style: {
                    width,
                    height: "100%",
                    borderRadius: "999rem",
                    backgroundColor: center ? "rgba(83, 177, 235, 0.86)" : "rgba(91, 222, 167, 0.9)"
                }
            })
        )
    );
}

function DriveView() {
    const driving = api.useValue(isDriving);
    const status = api.useValue(statusText);
    const name = api.useValue(possessedName);
    const control = api.useValue(controlStatus);
    const speed = clampNumber(api.useValue(speedMph), 0);
    const isBraking = api.useValue(braking);
    const reverse = api.useValue(reverseReady);
    const throttle = clampNumber(api.useValue(inputThrottle), 0);
    const brake = clampNumber(api.useValue(inputBrake), 0);
    const steering = clampNumber(api.useValue(inputSteering), 0);
    const fresh = api.useValue(inputFresh);
    const age = clampNumber(api.useValue(inputAgeSeconds), 0);
    const cameraEnabled = api.useValue(chaseCameraEnabled);
    const cameraStatus = api.useValue(chaseCameraStatus);
    const chaseEnabled = api.useValue(policeChaseEnabled);
    const chaseActive = api.useValue(policeChaseActive);
    const chaseStatus = api.useValue(policeChaseStatus);
    const chaseUnits = clampNumber(api.useValue(policeChaseUnits), 0);
    const violations = clampNumber(api.useValue(redLightViolations), 0);

    return h("div", null,
        h("div", {
            style: {
                justifyContent: "space-between",
                gap: "10rem",
                alignItems: "center",
                padding: "10rem",
                borderRadius: "8rem",
                backgroundColor: "rgba(255, 255, 255, 0.055)",
                border: "1rem solid rgba(255, 255, 255, 0.1)"
            }
        },
            h("div", { style: { minWidth: 0, flex: "1 1 auto" } },
                h("div", { style: { fontSize: "13rem", opacity: 0.7 } }, driving ? "Driving" : "Ready"),
                h("div", {
                    style: {
                        marginTop: "2rem",
                        fontSize: "22rem",
                        fontWeight: "800",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap"
                    }
                }, driving && name ? name : "No vehicle possessed"),
                h("div", { style: { marginTop: "5rem", opacity: 0.8, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" } }, control || status)
            ),
            h("div", { style: { textAlign: "right", minWidth: "82rem", flex: "0 0 auto" } },
                h("div", { style: { fontSize: "30rem", lineHeight: "30rem", fontWeight: "850" } }, Math.round(speed)),
                h("div", { style: { opacity: 0.72, fontSize: "12rem" } }, "mph")
            )
        ),
        h("div", { style: Object.assign({}, row, { marginTop: "10rem", flexWrap: "wrap" }) },
            h(Pill, { label: fresh ? "input live" : `input ${age.toFixed(1)}s`, tone: fresh ? "green" : "red" }),
            driving ? h(Pill, { label: isBraking ? "braking" : "rolling", tone: isBraking ? "amber" : "blue" }) : null,
            reverse ? h(Pill, { label: "reverse armed", tone: "amber" }) : null,
            driving && cameraEnabled ? h(Pill, { label: "chase camera", tone: "blue" }) : null,
            chaseActive ? h(Pill, { label: "police chase", tone: "red" }) : null,
            chaseEnabled && !chaseActive ? h(Pill, { label: "chase armed", tone: "amber" }) : null
        ),
        cameraEnabled ? h("div", {
            style: {
                marginTop: "8rem",
                opacity: 0.72,
                fontSize: "12rem",
                lineHeight: "1.2"
            }
        }, cameraStatus || "Chase camera ready") : null,
        chaseEnabled ? h("div", {
            style: {
                marginTop: "8rem",
                opacity: chaseActive ? 0.94 : 0.7,
                fontSize: "12rem",
                lineHeight: "1.2"
            }
        }, `${chaseStatus || "Police chase armed"}${chaseActive ? ` (${Math.round(chaseUnits)} units)` : ""}${violations > 0 ? ` | red runs ${Math.round(violations)}` : ""}`) : null,
        h("div", { style: Object.assign({}, row, { marginTop: "12rem" }) },
            h(Meter, { label: "Throttle", value: throttle }),
            h(Meter, { label: "Brake", value: brake }),
            h(Meter, { label: "Steer", value: steering, center: true })
        ),
        h("div", { style: Object.assign({}, row, { marginTop: "12rem" }) },
            driving
                ? commandButton("Release", () => trigger("Release"), Object.assign({ flex: 1 }, dangerButton))
                : commandButton("Possess car", () => trigger("ToggleDriving"), Object.assign({ flex: 1 }, primaryButton)),
            driving ? commandButton("Start Chase", () => trigger("StartPoliceChaseTest"), { minWidth: "108rem" }) : null,
            commandButton("Reset", () => trigger("ResetSettings"), { minWidth: "84rem" })
        ),
        h("div", { style: { marginTop: "12rem" } },
            h(ToggleRow, { binding: roadIntentAssist, action: "SetRoadIntentAssist", label: "Road intent", hint: "AI turn choice from left/right input" }),
            h(ToggleRow, { binding: roadHeightAssist, action: "SetRoadHeightAssist", label: "Road attach", hint: "Keeps the body on the lane height" }),
            h(ToggleRow, { binding: freezeVanillaNavigation, action: "SetFreezeVanillaNavigation", label: "Freeze AI body path", hint: "Stops vanilla movement fighting player control" }),
            h(ToggleRow, { binding: chaseCameraEnabled, action: "SetChaseCameraEnabled", label: "Chase camera", hint: "Attached behind-car camera" }),
            h(ToggleRow, { binding: policeChaseEnabled, action: "SetPoliceChaseEnabled", label: "Police chase", hint: "Starts after running a red without stopping" })
        )
    );
}

function TuningView() {
    return h("div", null,
        h(SliderRow, { binding: targetSpeedMph, action: "SetTargetSpeedMph", label: "Forward speed", step: 1, unit: " mph" }),
        h(SliderRow, { binding: reverseSpeedMph, action: "SetReverseSpeedMph", label: "Reverse speed", step: 1, unit: " mph" }),
        h(SliderRow, { binding: accelerationMps2, action: "SetAccelerationMps2", label: "Launch response", step: 0.5, format: value => value.toFixed(1) }),
        h(SliderRow, { binding: brakeMps2, action: "SetBrakeMps2", label: "Brake response", step: 0.5, format: value => value.toFixed(1) }),
        h(SliderRow, { binding: coastMps2, action: "SetCoastMps2", label: "Coast slowdown", step: 0.5, format: value => value.toFixed(1) }),
        h(SliderRow, { binding: reverseAccelerationMps2, action: "SetReverseAccelerationMps2", label: "Reverse response", step: 0.5, format: value => value.toFixed(1) }),
        h(SliderRow, { binding: maxTurnDegPerSecond, action: "SetMaxTurnDegPerSecond", label: "Steering response", step: 1, unit: " deg/s" }),
        h(SliderRow, { binding: lowSpeedTurnBoost, action: "SetLowSpeedTurnBoost", label: "Low-speed turn boost", step: 0.01, format: value => value.toFixed(2) }),
        h(SliderRow, { binding: roadHeightStickiness, action: "SetRoadHeightStickiness", label: "Road attach strength", step: 0.01, format: value => value.toFixed(2) }),
        h(SliderRow, { binding: chaseCameraDistance, action: "SetChaseCameraDistance", label: "Camera distance", step: 0.5, format: value => value.toFixed(1), unit: " m" }),
        h(SliderRow, { binding: chaseCameraHeight, action: "SetChaseCameraHeight", label: "Camera height", step: 0.25, format: value => value.toFixed(2), unit: " m" }),
        h(SliderRow, { binding: chaseCameraLookAhead, action: "SetChaseCameraLookAhead", label: "Camera look ahead", step: 0.5, format: value => value.toFixed(1), unit: " m" })
    );
}

function BetaTestDrivingButton() {
    const visible = api.useValue(panelVisible);

    return h(ui.Tooltip, {
        tooltip: h("div", { style: { padding: "4rem 0" } },
            h("div", { style: { fontWeight: "700", marginBottom: "2rem" } }, "Beta Test Driving Mod"),
            h("div", null, visible ? "Hide driving panel" : "Open driving panel")
        )
    },
        h(ui.Button, {
            src: icon,
            selected: !!visible,
            variant: "floating",
            onSelect: togglePanelFromButton,
            onClick: togglePanelFromButton,
            title: "Beta Test Driving Mod"
        })
    );
}

function BetaTestDrivingPanel() {
    const visible = api.useValue(panelVisible);
    const [tab, setTab] = React.useState("drive");

    if (!visible) {
        return null;
    }

    return h("div", { style: panel },
        h("div", { style: header },
            h("img", {
                src: icon,
                style: {
                    width: "34rem",
                    height: "34rem",
                    borderRadius: "7rem",
                    backgroundColor: "rgba(255, 255, 255, 0.08)"
                }
            }),
            h("div", { style: { flex: 1, minWidth: 0 } },
                h("div", { style: { fontSize: "15rem", fontWeight: "800" } }, "Beta Test Driving Mod"),
                h("div", { style: { marginTop: "1rem", opacity: 0.7, fontSize: "12rem" } }, "Direct Drive")
            ),
            h("button", {
                style: Object.assign({}, button, { minWidth: "30rem", padding: "3rem 8rem" }),
                onClick: () => trigger("SetPanelVisible", false)
            }, "x")
        ),
        h("div", { style: { padding: "10rem 12rem 0" } },
            h("div", { style: Object.assign({}, row, { padding: "3rem", borderRadius: "8rem", backgroundColor: "rgba(255, 255, 255, 0.055)" }) },
                h(TabButton, { id: "drive", selected: tab === "drive", setTab }, "Drive"),
                h(TabButton, { id: "tune", selected: tab === "tune", setTab }, "Tuning")
            )
        ),
        h("div", { style: content }, tab === "drive" ? h(DriveView) : h(TuningView))
    );
}

function extension(registry) {
    registry.append("GameTopLeft", BetaTestDrivingButton);
    registry.append("Game", BetaTestDrivingPanel);
}

const hasCSS = false;

export { hasCSS, extension as default };
