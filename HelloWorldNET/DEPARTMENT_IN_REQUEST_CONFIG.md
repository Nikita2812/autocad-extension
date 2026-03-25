# Department Configuration in Request - Implementation Guide

## What Changed

The fingerprinting engine now **reads the department from the request configuration file**. This gives you complete control over block classification per extraction request.

---

## Request Configuration Structure

### Required Field: `department`

Add the `department` field to your request JSON:

```json
{
  "drawing_path": "C:/drawings/plant.dwg",
  "project_id": "PROJECT-123",
  "participant_id": "user@company.com",
  "department": "Piping"
}
```

### Allowed Values

```
"Piping"              ← Piping and fluid systems
"Mechanical"         ← Mechanical equipment
"Electrical"         ← Electrical systems
"Instrumentation"    ← Instruments and sensors
"Civil"              ← Structural and civil
(leave empty or null for auto-detect from layer name)
```

---

## How It Works

### Priority Order

When determining which fingerprinting rules to apply:

1. **First**: Check if `department` is provided in request config
   - If yes → Use it (rules filtered by that department)
   - If no → Fall back to layer-based detection

2. **Second**: Auto-detect from block's layer name (fallback)
   - Example: Layer "P_VALVES" → Department "Piping"

3. **Result**: Only rules matching the determined department are evaluated

---

## Example Flows

### Flow 1: Explicit Department in Request (Preferred)

```
Request Config:
{
  "drawing_path": "C:/drawings/plant.dwg",
  "department": "Piping"
}

↓

Block Processing:
Layer "P_VALVES" (anonymous block with 1 circle + 2 lines)

↓

Fingerprinting:
1. Department = "Piping" (from request) ✓
2. Filter rules → Only Piping rules apply (8 rules)
3. Geometry match → VALVE_CHECK ✓
4. Result: "VALVE_CHECK"

Command Output:
"→ Department: Piping (from request)"
```

### Flow 2: Auto-Detect from Layer (Fallback)

```
Request Config:
{
  "drawing_path": "C:/drawings/plant.dwg"
  // No department field
}

↓

Block Processing:
Layer "P_VALVES" (anonymous block)

↓

Fingerprinting:
1. Department = null (not in request)
2. Auto-detect from layer "P_VALVES" → "Piping"
3. Filter rules → Only Piping rules apply (8 rules)
4. Geometry match → VALVE_CHECK ✓
5. Result: "VALVE_CHECK"

Command Output:
"→ Department: Piping (from layer)"
```

### Flow 3: Cross-Department (Same Block, Different Results)

```
SCENARIO A: Same block, Piping department
Request:
{
  "department": "Piping"
}
Geometry: 1 circle + 2 lines
Fingerprint Result: "VALVE_CHECK"

────────────────────────────────────────

SCENARIO B: Same block, Electrical department
Request:
{
  "department": "Electrical"
}
Geometry: 1 circle + 2 lines (same!)
Fingerprint Result: "MOTOR_AC"

═════════════════════════════════════════
SAME GEOMETRY, DIFFERENT DEPARTMENTS = DIFFERENT RESULTS
```

---

## Implementation Details

### In ExtractDrawingJsonOnly()

The bridge-driven extraction mode reads department from request:

```csharp
// Read department from request config
string department = GetSilentConfigValue(config, "department", null);

// Use it when fingerprinting anonymous blocks
string assetType = FingerprintAnonymousBlock(blockRef.BlockId, tr, ed, department);
```

### In ExtractDrawingJsonSilent()

The silent async extraction mode also reads department:

```csharp
// Read department from request config
string department = GetSilentConfigValue(config, "department", null);

// Pass to fingerprinting engine
string fingerprintResult = FingerprintAnonymousBlock(blockRef.BlockId, tr, ed, department);
```

### In FingerprintAnonymousBlock()

The matching engine prioritizes request department:

```csharp
// Priority: Request department > Auto-detect from layer
string blockDepartment = providedDepartment;
string departmentSource = "request";

if (string.IsNullOrWhiteSpace(blockDepartment))
{
    blockDepartment = DetermineDepartmentFromEntity(blockRecordId, tr);
    departmentSource = "layer";
}

// Log which source was used
if (ed != null)
    ed.WriteMessage($"\n  → Department: {blockDepartment} (from {departmentSource})");
```

---

## Complete Request Examples

### Example 1: Piping Project

```json
{
  "drawing_path": "C:/projects/refinery/process_diagram.dwg",
  "project_id": "PROJ-2024-001",
  "participant_id": "engineer@company.com",
  "department": "Piping",
  "api_url": "https://api.example.com/review"
}
```

**Result**: All blocks classified using Piping rules (VALVE_GATE, PUMP_CENTRIFUGAL, TEE_FITTING, etc.)

### Example 2: Electrical Project

```json
{
  "drawing_path": "C:/projects/power_plant/distribution.dwg",
  "project_id": "PROJ-2024-002",
  "participant_id": "electrician@company.com",
  "department": "Electrical",
  "api_url": "https://api.example.com/review"
}
```

**Result**: All blocks classified using Electrical rules (MOTOR_AC, BREAKER_CIRCUIT, TRANSFORMER)

### Example 3: Mixed Plant (Multi-Department)

```json
{
  "drawing_path": "C:/projects/chemical_plant/full_design.dwg",
  "project_id": "PROJ-2024-003",
  "participant_id": "designer@company.com",
  "department": "Mechanical",
  "api_url": "https://api.example.com/review"
}
```

**Result**: All blocks classified using Mechanical rules (Electrical and Piping rules ignored, even if geometries match)

---

## Command-Line Output

### With Request Department

```
Department: Piping (from request)
Matches (8 rules evaluated): VALVE_GATE / VALVE_CHECK (Circles=1, Lines=3, Polylines=1, Arcs=0, Hatches=0, Texts=0)
```

### With Auto-Detected Department

```
Department: Piping (from layer P_VALVES)
Matches (8 rules evaluated): VALVE_CHECK (Circles=1, Lines=2, Polylines=0, Arcs=0, Hatches=0, Texts=0)
```

---

## Best Practices

### 1. Always Specify Department in Request (Recommended)

```json
{
  "department": "Piping"  ← Explicitly set for clarity
}
```

**Why**: Makes extraction deterministic and predictable. No reliance on layer naming conventions.

### 2. Match Department to Project Context

```json
{
  "department": "Electrical"  ← Matches the project you're extracting
}
```

**Why**: Ensures correct asset type classification.

### 3. Document Department Mapping in Client Documentation

For your team/clients:
```
"Piping"         → Process piping, equipment, valves, fittings, flanges
"Mechanical"     → Pumps, compressors, motors, turbines
"Electrical"     → Power, motors, control systems, circuits
"Instrumentation" → Sensors, bubbles, measurement points
"Civil"          → Structural, foundations, supports
```

---

## Testing Scenarios

### Test 1: Verify Department from Request is Used

**Setup**:
- Draw a block with 1 circle + 2 lines
- Place in layer "P_VALVES"
- Request with department="Electrical"

**Expected**:
- Should match MOTOR_AC (Electrical rule)
- NOT VALVE_CHECK (Piping rule)
- Output: "from request"

### Test 2: Verify Fallback to Layer Detection

**Setup**:
- Same block and layer
- Request WITHOUT department field

**Expected**:
- Should match VALVE_CHECK (Piping rule)
- Layer "P_VALVES" triggers "Piping" auto-detect
- Output: "from layer"

### Test 3: Verify No Cross-Department Matches

**Setup**:
- Block with 2 circles (ambiguous)
- Could be REDUCER_ECCENTRIC (Piping) or TRANSFORMER (Electrical)
- Request with department="Mechanical"

**Expected**:
- No matches (no Mechanical rules have 2 circles)
- Result: "UNKNOWN_COMPONENT"

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Block classified as wrong type | Wrong department in request | Verify department matches drawing content |
| Block is UNKNOWN_COMPONENT | Department matches but geometry doesn't | Check block geometry against fingerprint rules |
| Department from layer, not request | Null/empty department in request | Add `"department": "..."` to request JSON |
| All blocks UNKNOWN_COMPONENT | Department set but no rules for it | Add rules for that department to fingerprints.json |

---

## JSON Validation

### Valid Request

```json
{
  "drawing_path": "C:/path/to/file.dwg",
  "department": "Piping",
  "project_id": "PROJ-123"
}
```

### Invalid (Will Fall Back to Layer Detection)

```json
{
  "drawing_path": "C:/path/to/file.dwg",
  // "department" field missing - will auto-detect
  "project_id": "PROJ-123"
}
```

### Invalid (Will Trigger Error)

```json
{
  "drawing_path": "C:/path/to/file.dwg",
  "department": "InvalidDepartment",  ← Not in allowed list
  "project_id": "PROJ-123"
}
```

**Behavior**: Falls back to layer-based detection (invalid department is treated as null/empty)

---

## Files Modified

| File | Change | Details |
|------|--------|---------|
| MyComm.cs | Updated FingerprintAnonymousBlock() | Now accepts `providedDepartment` parameter |
| MyComm.cs | Updated ExtractDrawingJsonOnly() | Reads department from request config |
| MyComm.cs | Updated ExtractDrawingJsonSilent() | Reads department from request config |
| MyComm.cs | Updated BlockReference handling | Passes department when fingerprinting |

---

## Build Status

✅ **Build successful**
- All changes compile without errors
- No warnings
- Backward compatible (department optional)

---

## Summary

| Aspect | Details |
|--------|---------|
| **Feature** | Department-based fingerprinting with request override |
| **Default** | Auto-detect from layer name if not specified |
| **Priority** | Request department > Layer-based detection |
| **Benefit** | Explicit control over asset classification |
| **Flexibility** | Same code, different departments = different results |
| **Backward Compatible** | Yes - department is optional |
| **Status** | ✅ Implemented and ready |

---

## Next Steps

1. ✅ Add `"department"` field to your request JSON files
2. ✅ Set it to the appropriate department for each project
3. ✅ Verify classification results
4. ✅ Adjust fingerprint rules if needed

**Recommended**: Always include department in request for deterministic, predictable behavior.
