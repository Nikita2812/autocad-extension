# Fingerprinting Engine - Administrator Configuration Guide

## Overview

The **Dynamic Fingerprinting Engine** allows you to classify anonymous AutoCAD blocks into semantic asset types **without modifying or recompiling C# code**. All configuration lives in an external JSON file (`fingerprints.json`).

### Core Principle
> Add a new block classification rule to `fingerprints.json`, and the system instantly learns on the next run. **Zero recompilation required.**

---

## How It Works

### 1. **Block Geometry Tally**
When the system encounters an anonymous block (or a block you want to classify), it tallies the geometric primitives inside:
- **Circles**: How many circle entities
- **Lines**: How many line entities
- **Polylines**: How many polyline entities
- **Arcs**: How many arc entities
- **Hatches**: How many hatch entities
- **Texts**: How many text entities (DBText + MText combined)

### 2. **Rule Matching (Priority-Based)**
The engine evaluates the tally against rules in order of `priority` (1 = highest):
- **Rule 1 (Priority=1)**: Check all constraints. If match → return assigned_name
- **Rule 2 (Priority=2)**: Check all constraints. If match → return assigned_name
- ...continue until match found...
- **Fallback**: If no rule matches → return `UNKNOWN_COMPONENT`

### 3. **Constraint Evaluation**
For each rule, ALL constraints must be satisfied:
- `"min_circles": 1` = block must have ≥1 circle
- `"max_circles": 1` = block must have ≤1 circle
- `null` or missing = no constraint on that geometry type

---

## Configuration File: `fingerprints.json`

### File Location
Place `fingerprints.json` in the same directory as the AutoCAD plugin DLL:
```
C:\Users\<user>\AppData\Roaming\Autodesk\AutoCAD 2024\Plug-ins\
HelloWorldNET\fingerprints.json
```

Or in the temp directory:
```
%TEMP%\ai_review\fingerprints.json
```

### Schema

```json
{
  "fingerprint_rules": [
    {
      "assigned_name": "INSTRUMENT_BUBBLE",
      "description": "Instrument circle with tag/label (P-101, T-201, etc.)",
      "geometry_match": {
        "min_circles": 1,
        "max_circles": 1,
        "min_texts": 1,
        "max_texts": 2
      },
      "priority": 1
    },
    {
      "assigned_name": "VALVE_GATE",
      "description": "Gate valve: 2 polylines + circle",
      "geometry_match": {
        "min_polylines": 2,
        "max_polylines": 2,
        "min_circles": 1,
        "max_circles": 1
      },
      "priority": 2
    }
  ]
}
```

### Field Definitions

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `assigned_name` | string | Yes | The semantic asset type to assign (e.g., "INSTRUMENT_BUBBLE", "VALVE_GATE") |
| `description` | string | No | Human-readable explanation of what this block represents |
| `priority` | int | Yes | Rule evaluation order. Lower number = higher priority (1=first). Must be unique. |
| `geometry_match` | object | No | Geometric constraints (all are optional). Only set constraints you care about. |

### Geometry Constraint Fields (All Optional)

For each geometry type, you can specify `min_` and `max_` constraints:

```json
"geometry_match": {
  "min_circles": 1,      // Block must have at least 1 circle
  "max_circles": 1,      // Block must have at most 1 circle
  "min_lines": 0,
  "max_lines": 5,
  "min_polylines": 2,
  "max_polylines": 4,
  "min_arcs": 0,
  "max_arcs": 2,
  "min_hatches": 0,
  "max_hatches": 1,
  "min_texts": 1,        // Block must have at least 1 text element
  "max_texts": 3
}
```

### Important Notes
- **Omit a constraint if you don't care about it.** An empty `geometry_match: {}` means "match any block"
- **All specified constraints must be satisfied** for the rule to match
- **Rules are evaluated in priority order** (lowest priority number first)
- **First match wins** – once a rule matches, no further rules are evaluated

---

## Real-World Examples

### Example 1: Instrument Bubble (Circle + Text)
A typical instrument symbol in P&IDs: a circle with a tag like "P-101" or "T-201"

```json
{
  "assigned_name": "INSTRUMENT_BUBBLE",
  "description": "Instrument circle with tag/label text",
  "geometry_match": {
    "min_circles": 1,
    "max_circles": 1,
    "min_texts": 1,
    "max_texts": 2
  },
  "priority": 1
}
```

### Example 2: Gate Valve
A gate valve is typically drawn as 2 polylines (the body and the stem) plus a circle (the connection point):

```json
{
  "assigned_name": "VALVE_GATE",
  "description": "Gate valve: polyline body + stem + circle",
  "geometry_match": {
    "min_polylines": 2,
    "max_polylines": 3,
    "min_circles": 1,
    "max_circles": 1,
    "min_lines": 0,
    "max_lines": 2
  },
  "priority": 2
}
```

### Example 3: Y-Strainer (Complex Block)
A Y-strainer might have hatching (to show internal filtering) plus multiple lines:

```json
{
  "assigned_name": "STRAINER_Y",
  "description": "Y-strainer: hatch + multiple lines",
  "geometry_match": {
    "min_hatches": 1,
    "min_lines": 3,
    "max_lines": 6
  },
  "priority": 5
}
```

### Example 4: Tee Fitting (Permissive)
A tee fitting is flexible in shape; we just want to avoid matching other equipment:

```json
{
  "assigned_name": "TEE_FITTING",
  "description": "Tee connector: 2-3 polylines, no circles",
  "geometry_match": {
    "min_polylines": 2,
    "max_polylines": 3,
    "max_circles": 0
  },
  "priority": 10
}
```

---

## Workflow: Adding a New Block Type

Scenario: The AI flags an `UNKNOWN_COMPONENT`, and you want to teach the system a new block type.

### Step 1: Inspect the Block in AutoCAD
Open the drawing and look at the block:
- Count the **circles** inside
- Count the **lines** inside
- Count the **polylines** inside
- Count the **arcs** inside
- Count the **hatches** inside
- Count the **text elements** inside

### Step 2: Edit `fingerprints.json`
Add a new rule entry:

```json
{
  "assigned_name": "MY_NEW_BLOCK_TYPE",
  "description": "Describe what this block represents",
  "geometry_match": {
    "min_circles": <X>,
    "max_circles": <Y>,
    // ... other constraints ...
  },
  "priority": <next_priority_number>
}
```

### Step 3: Assign a Priority
- Use the next available priority number after the highest existing one
- Lower numbers = evaluated first (higher priority)
- Typical range: 1-50

### Step 4: Test
Run the extraction command again. The block should now be fingerprinted with your new assigned_name.

### Example: Teaching the System "COMPRESSOR"
```json
{
  "assigned_name": "COMPRESSOR",
  "description": "Rotary or reciprocating compressor: circle + 3 lines",
  "geometry_match": {
    "min_circles": 1,
    "max_circles": 1,
    "min_lines": 3,
    "max_lines": 4
  },
  "priority": 15
}
```

---

## Advanced: Client-Specific Configurations

Different EPCM firms draw symbols differently. You can maintain multiple fingerprints files:

```
fingerprints_fluor.json      ← Fluor standards
fingerprints_bechtel.json    ← Bechtel standards
fingerprints_internal.json   ← Your company standards
```

Load the appropriate one based on project context:

```csharp
// In MyComm.cs or your caller code
string clientFingerprints = "fingerprints_fluor.json"; // or get from config
configMgr.LoadFingerprintRules(clientFingerprints);
```

---

## Debugging: Unmatched Blocks

If a block doesn't match any rule, it gets labeled `UNKNOWN_COMPONENT`. To debug:

1. **Check the logs**: Look for "Unmatched geometry: Circles=X, Lines=Y, ..." messages
2. **Verify JSON syntax**: Make sure `fingerprints.json` is valid JSON
3. **Check priorities**: Ensure no two rules have the same priority
4. **Verify constraints**: Confirm your min/max constraints are correct for what you observed

### Enable Logging (Future Enhancement)
The fingerprints.json file structure supports future logging flags:
```json
{
  "enable_logging": true,
  "log_unmatched_blocks": true,
  "fingerprint_rules": [ ... ]
}
```

---

## Summary: Zero-Downtime Workflow

1. ✅ AI encounters `UNKNOWN_COMPONENT` in a drawing
2. ✅ You inspect the block geometry in AutoCAD
3. ✅ You add 5 lines to `fingerprints.json`
4. ✅ You save `fingerprints.json`
5. ✅ Next extraction run: block is automatically classified
6. ✅ **No C# recompilation. No plugin restart. No downtime.**

This is enterprise-grade maintainability.

---

## Testing with Python

You can validate your fingerprints rules by simulating the matching in Python:

```python
import json

# Load rules
with open('fingerprints.json') as f:
    config = json.load(f)

# Simulate a block with certain geometry
block_geometry = {
    'circles': 1,
    'lines': 0,
    'polylines': 0,
    'arcs': 0,
    'hatches': 0,
    'texts': 2
}

# Find matching rule
for rule in sorted(config['fingerprint_rules'], key=lambda r: r['priority']):
    match_spec = rule['geometry_match']
    
    # Check all constraints
    if all(
        (match_spec.get(f'min_{geo}') is None or block_geometry[geo] >= match_spec[f'min_{geo}']) and
        (match_spec.get(f'max_{geo}') is None or block_geometry[geo] <= match_spec[f'max_{geo}'])
        for geo in block_geometry.keys()
    ):
        print(f"✓ Match: {rule['assigned_name']}")
        break
else:
    print("✗ No match: UNKNOWN_COMPONENT")
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Rules not loading | Verify fingerprints.json exists and is valid JSON |
| All blocks get `UNKNOWN_COMPONENT` | Check that at least one rule's constraints are satisfied |
| Wrong asset assigned | Lower-priority rule is matching first; reorder priorities |
| Block geometry count wrong | Some entity types (e.g., spline, insert) aren't tallied; consider only Circle/Line/Polyline/Arc/Hatch/Text |

---

## Version History

- **v1.0** (Initial Release)
  - Priority-based rule matching
  - Support for 6 geometry types (circles, lines, polylines, arcs, hatches, texts)
  - min/max constraints per geometry type
  - In-memory rule caching for performance
