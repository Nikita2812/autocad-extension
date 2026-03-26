# fingerprints.json - Configuration Guide

## Quick Reference: How to Debug & Fix Unrecognized Blocks

### Situation 1: Block Outputs as Anonymous (e.g., "A$C157444E+3")

**This means:** The geometry didn't match ANY rule in this file.

**Solution:**

1. **Find the most similar rule**
   ```json
   {
     "assigned_name": "STRAINER_Y",
     "geometry_match": {
       "min_hatches": 1,
       "min_lines": 2,
       "min_polylines": 1
     }
   }
   ```

2. **Make it less restrictive**
   - Change `"min_hatches": 1` to `"min_hatches": 0` (or delete the line)
   - Reduce any `min_*` values that might be too strict
   - Increase any `max_*` values if needed

3. **Save and test immediately**
   - No recompilation required
   - Extraction will recognize the block on next run

---

## Rule Structure Explained

```json
{
  "assigned_name": "CHECK_VALVE",         ← What you want to call this block
  "description": "Check valve (circle with internal line)",  ← For documentation
  "department": "PROCESS",                ← Scope: "PROCESS", "MECHANICAL", "ELECTRICAL", "I&C", "CSA", or "" (all)
  "geometry_match": {                     ← Rules that determine if geometry matches
    "min_circles": 1,                     ← At least 1 circle
    "max_circles": 1,                     ← At most 1 circle
    "min_lines": 1,                       ← At least 1 line
    "max_lines": 2                        ← At most 2 lines
  }
}
```

### Geometry Types Counted

- `circles` - Round shapes
- `lines` - Straight line segments
- `polylines` - Continuous line paths
- `arcs` - Curved segments
- `hatches` - Filled/patterned areas
- `texts` - Text labels inside the block

### Department Scope

- `"PROCESS"` - Only apply to blocks in piping/process layers
- `"MECHANICAL"` - Only apply to mechanical equipment
- `"ELECTRICAL"` - Only apply to electrical components
- `"I&C"` - Only apply to instrumentation
- `"CSA"` - Only apply to structural/civil
- `""` (empty string) - Apply to ALL departments (catch-all)

---

## Common Patterns

### Pattern 1: Simple Instrument Tag (Circle with Text)

```json
{
  "assigned_name": "INSTRUMENT_TAG",
  "description": "Instrument circle (P-101, T-201, etc.)",
  "department": "I&C",
  "geometry_match": {
    "min_circles": 1,
    "max_circles": 1,
    "min_texts": 1,
    "max_texts": 2
  }
}
```

**Matches:** A circle with 1-2 text labels inside (like "P-101A")

### Pattern 2: Valve Symbol (Lines + Polylines)

```json
{
  "assigned_name": "VALVE_GATE",
  "description": "Gate valve (diamond with lines)",
  "department": "PROCESS",
  "geometry_match": {
    "min_polylines": 1,
    "max_polylines": 3,
    "min_lines": 2,
    "max_lines": 4
  }
}
```

**Matches:** 1-3 polylines + 2-4 lines (typical valve symbol)

### Pattern 3: Pump (Circle + Flow Indicator)

```json
{
  "assigned_name": "PUMP_CENTRIFUGAL",
  "description": "Pump (circle with arrow)",
  "department": "MECHANICAL",
  "geometry_match": {
    "min_circles": 1,
    "max_circles": 1,
    "min_lines": 2,
    "max_lines": 4
  }
}
```

**Matches:** Single circle + 2-4 lines (arrow or flow indicator)

### Pattern 4: Strainer (Complex Geometry)

```json
{
  "assigned_name": "STRAINER_Y",
  "description": "Y-strainer",
  "department": "PROCESS",
  "geometry_match": {
    "min_hatches": 1,
    "min_lines": 2,
    "min_polylines": 1
  }
}
```

**Matches:** At least 1 hatch pattern + 2+ lines + 1+ polylines

---

## Debugging: Enable Logging

Add to the top level of `fingerprints.json`:

```json
{
  "enable_logging": true,
  "log_unmatched_blocks": true,
  
  "fingerprint_rules": [
    { ... your rules ... }
  ],
  
  "fallback_strategy": "UNKNOWN_COMPONENT"
}
```

This will print debug messages to the AutoCAD Output window.

---

## If a Rule Isn't Matching

### Check 1: Are your min/max values correct?

**Too Restrictive:**
```json
"geometry_match": {
  "min_hatches": 1,           ← Your block might have 0 hatches
  "max_circles": 1,           ← Your block might have 2 circles
  "min_lines": 5              ← Your block might have only 3 lines
}
```

**Better (More Flexible):**
```json
"geometry_match": {
  "min_hatches": 0,           ← Accept blocks with no hatches
  "max_circles": 2,           ← Accept up to 2 circles
  "min_lines": 2              ← Only require at least 2 lines
}
```

### Check 2: Is the department filter excluding your block?

If your block is on layer `M_EQUIP` (Mechanical) but the rule says `"department": "PROCESS"`, it won't match.

**Solution:** Either:
- Add a new rule with the correct department
- Change rule department to `""` (match all departments)

### Check 3: Create a new rule specifically for your block

When in doubt, add a new rule that exactly matches what you see:

```json
{
  "assigned_name": "MY_CUSTOM_BLOCK",
  "description": "My custom block that appeared",
  "department": "",
  "geometry_match": {
    "min_lines": 3,
    "max_lines": 5,
    "min_circles": 1,
    "max_circles": 1
  }
}
```

---

## Testing Your Changes

1. **Edit a rule in `fingerprints.json`**
2. **Save the file** (make sure valid JSON syntax)
3. **In AutoCAD, run extraction:**
   ```
   Command: extractJSONOnly
   ```
4. **Check output JSON** - do you see your semantic name instead of `A$*`?
5. **If not matching:**
   - Make rules even MORE flexible (reduce min_*, increase max_*)
   - Add more rule examples
   - Check debug logs in Output window

---

## Validation Checklist

Before using your updated `fingerprints.json`:

- ✅ Valid JSON syntax (use an online JSON validator if unsure)
- ✅ All `assigned_name` values are meaningful (no empty strings)
- ✅ At least one `geometry_match` condition per rule
- ✅ `min_*` values are ≤ the corresponding `max_*` values
- ✅ Department values match those in your layer naming convention
- ✅ File is saved as UTF-8 without BOM (like the current file)

---

## Real-World Example: Fixing "STRAINER" Recognition

**Situation:** Block appears as `"A$C157444E+3"` instead of `"STRAINER"`

**Step 1: Run extraction and note the geometry**
```
Command: extractJSONOnly
(Check log for geometry tally: "Circles: 0, Lines: 3, Polylines: 1, Hatches: 0")
```

**Step 2: Find strainer rule**
```json
{
  "assigned_name": "STRAINER_Y",
  "geometry_match": {
    "min_hatches": 1,  ❌ Problem: requires 1 hatch, but geometry has 0
    "min_lines": 2,
    "min_polylines": 1
  }
}
```

**Step 3: Update rule**
```json
{
  "assigned_name": "STRAINER_Y",
  "geometry_match": {
    "min_hatches": 0,  ✅ Fixed: now accepts 0 hatches
    "min_lines": 2,
    "min_polylines": 1
  }
}
```

**Step 4: Save and test**
```
Command: extractJSONOnly
(Now outputs: "block_name": "STRAINER_Y" ✅)
```

---

## Performance Tips

- **Don't make rules too loose** - they should be specific enough to avoid false positives
- **Order rules from most specific to most general** - more specific patterns first
- **Use department filters** - reduces number of rules checked per block
- **Regularly review `log_unmatched_blocks`** - add rules for common unmatched geometries

---

## Questions?

- **Block not recognized?** → Loosen the rule (reduce min_*, increase max_*)
- **Wrong block type?** → Create a more specific rule or change department filter
- **Configuration syntax error?** → Use online JSON validator, ensure commas between rules
- **Need to add new block type?** → Copy an existing rule, modify geometry_match and assigned_name

Remember: **Save → Extract → Check output → Adjust → Save again** (no recompilation needed!)
