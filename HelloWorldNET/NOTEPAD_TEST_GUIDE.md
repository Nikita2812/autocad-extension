# 5-Second Notepad Verification Test

## The Problem
AutoCAD was exporting anonymous block names like `A$C157444E+3` instead of semantic names like `STRAINER`.

## The Fix
The code now checks a dictionary before exporting. If the block is in the dictionary, it uses the mapped name. Otherwise, it uses fingerprinting.

---

## The 5-Second Test

### Step 1: Generate JSON
```
In AutoCAD:
Command: extractJSON
Answer: Project ID? (any value or press Enter)
Answer: Participant ID? (any value or press Enter)

File created: drawing_data.json (in same folder as your drawing)
```

### Step 2: Open in Notepad
```
Windows File Explorer:
Right-click drawing_data.json
Choose: Open With → Notepad

OR:

Command line:
notepad "C:\path\to\drawing_data.json"
```

### Step 3: Search for A$ Strings
```
In Notepad:
Press Ctrl+F
Search for: A$
```

---

## What the Results Mean

### If Notepad Finds "A$C157444E+3"

```
Notepad: "Found 3 matches for A$"
```

**This means:** ❌ The fix is NOT working yet

### If Notepad Says "Cannot Find A$"

```
Notepad: "Cannot find A$"
```

**This means:** ✅ The fix IS working! The A$ blocks are gone!

---

## What Could Be Wrong?

### Scenario 1: Found A$ Strings

**Possible causes:**
1. ❌ You didn't rebuild the project after editing the dictionary
2. ❌ You didn't NETLOAD the new DLL into AutoCAD (still using old version)
3. ❌ The dictionary is empty (no mappings added)
4. ❌ The anonymous block name in your drawing doesn't match any dictionary key

**Fix:**
1. Make sure you updated `blockDictionary` in MyComm.cs
2. Visual Studio → Build → Rebuild Solution
3. AutoCAD → NETLOAD → select the updated DLL
4. Extract JSON again
5. Run Notepad test again

### Scenario 2: Cannot Find A$ (Great!)

**Now search for your semantic names:**
```
In Notepad:
Press Ctrl+F
Search for: STRAINER
```

If found → The dictionary mapping worked! ✅

If NOT found → The block might not be in your drawing, or the dictionary key doesn't match exactly

---

## Detailed Search Examples

### Example 1: Strainer Block

**Before Fix:**
```json
{
  "block_name": "A$C157444E+3",
  "asset_type": "Equipment"
}
```

**After Fix (with dictionary):**
```json
{
  "block_name": "STRAINER",
  "asset_type": "Equipment"
}
```

**Test:**
- Notepad search for `A$C157444E+3` → NOT found ✓
- Notepad search for `STRAINER` → FOUND ✓

### Example 2: Pressure Gauge Block

**Before Fix:**
```json
{
  "block_name": "*X109",
  "asset_type": "Equipment"
}
```

**After Fix (with dictionary):**
```json
{
  "block_name": "PRESSURE_GAUGE",
  "asset_type": "Equipment"
}
```

**Test:**
- Notepad search for `*X109` → NOT found ✓
- Notepad search for `PRESSURE_GAUGE` → FOUND ✓

---

## Multiple Test Cases

Test each anonymous block you added to the dictionary:

| Raw Name | Search For | Expected Result | Status |
|----------|-----------|-----------------|--------|
| A$C157444E+3 | A$C157444E | NOT found | ✓ |
| A$C04F129AC | A$C04F129AC | NOT found | ✓ |
| *X109 | *X109 | NOT found | ✓ |
| - | STRAINER | FOUND | ✓ |
| - | PRESSURE_GAUGE | FOUND | ✓ |
| - | CHECK_VALVE | FOUND | ✓ |

---

## Common Mistakes

### ❌ Mistake 1: Searching for the mapped value
```
You added: { "A$C157444E+3", "STRAINER" }
You search for: A$C157444E
Expected: NOT found
But you searched for the wrong string!
```

### ❌ Mistake 2: Looking in the wrong file
```
You have:
  - drawing_data.json ← Extract NEWER version
  - drawing_data_old.json ← Contains old A$ strings

Always use the most recently created drawing_data.json
```

### ❌ Mistake 3: AutoCAD still running old DLL
```
You changed MyComm.cs
You rebuilt the project
You opened drawing_data.json
BUT you didn't NETLOAD the new DLL!

AutoCAD will keep running the old, broken code from memory.
```

**Solution:** Always NETLOAD after rebuilding!

---

## Success Message

When you see this, the fix is working:

```
Notepad: "Cannot find A$"
AND
Notepad finds "STRAINER", "CHECK_VALVE", "PRESSURE_GAUGE", etc.
```

✅ **The system is now working correctly!**

