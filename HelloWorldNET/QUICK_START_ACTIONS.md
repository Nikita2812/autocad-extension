# Quick Start: Developer Action Items

## What Was Fixed
The code now **calls** the block dictionary before exporting JSON. Previously it was just skipping to fingerprinting.

## What YOU Need to Do
Populate the dictionary with YOUR block names and test it.

---

## Step-by-Step Actions

### ACTION 1: Extract a Test JSON
```
1. Open AutoCAD
2. Open your drawing
3. Type: extractJSON
4. Press Enter
5. Answer prompts (Project ID, Participant ID)
6. Result: drawing_data.json is created
```

### ACTION 2: Find Your Block Names
```
1. Open drawing_data.json in Notepad
2. Press Ctrl+F
3. Search for: A$
4. Write down EVERY block name you see:
   - A$C157444E+3
   - A$C04F129AC
   - *X109
   - etc.
```

### ACTION 3: Determine What Each Block Is
```
For each block name you found:
  
  Option A (Easiest):
  - Click on it in AutoCAD
  - Look at it visually
  - Decide: Is it a Strainer? Valve? Pump? Gauge?
  - Write down the semantic name
  
  Option B (Use debug info):
  - Look at "block_fingerprint" field in JSON
  - Use what the system guesses
```

### ACTION 4: Edit the Dictionary
```
1. Open Visual Studio
2. Open HelloWorldNET/MyComm.cs
3. Find this method (Ctrl+F, search for "LookupBlockTranslation"):

   private string LookupBlockTranslation(string anonymousName)
   {
       Dictionary<string, string> blockDictionary = new Dictionary<string, string>
       {
           // ← ADD YOUR MAPPINGS HERE
       };

4. Add your blocks like this:
   
   { "A$C157444E+3", "STRAINER" },
   { "A$C04F129AC", "CHECK_VALVE" },
   { "*X109", "PRESSURE_GAUGE" },
```

**CRITICAL:** Each key must be EXACTLY as it appears in the JSON, with correct case!

### ACTION 5: Compile and Reload
```
1. Visual Studio: Build → Rebuild Solution
2. Wait for "Build succeeded"
3. AutoCAD: Type NETLOAD
4. Select: HelloWorldNET\bin\Debug\HelloWorldNET.dll
5. Confirm: "Successfully loaded HelloWorldNET"
```

### ACTION 6: Test It
```
1. Run extractJSON again (same as ACTION 1)
2. Open drawing_data.json in Notepad
3. Press Ctrl+F
4. Search for: A$C157444E
5. Result:
   - "Cannot find A$C157444E" → ✅ FIX WORKS!
   - Found results → ❌ Need to fix something
```

---

## Example: Complete Dictionary

If your drawing has these blocks:

```
From Notepad search:
- A$C157444E+3
- A$C04F129AC  
- *X109
```

Your dictionary should look like:

```csharp
Dictionary<string, string> blockDictionary = new Dictionary<string, string>
{
    { "A$C157444E+3", "STRAINER" },
    { "A$C04F129AC", "CHECK_VALVE" },
    { "*X109", "PRESSURE_GAUGE" },
};
```

---

## Common Mistakes to AVOID

### ❌ MISTAKE 1: Case Sensitivity
```
WRONG:  { "a$c157444e+3", "STRAINER" }
RIGHT:  { "A$C157444E+3", "STRAINER" }
        ↑ Capital A, capital C, plus sign
```

### ❌ MISTAKE 2: Incomplete Keys
```
WRONG:  { "A$C157444E", "STRAINER" }
RIGHT:  { "A$C157444E+3", "STRAINER" }
        ↑ Includes the +3 at the end
```

### ❌ MISTAKE 3: Missing Comma
```
WRONG:
{ "A$C157444E+3", "STRAINER" }
{ "A$C04F129AC", "CHECK_VALVE" }
↑ Missing comma on first line

RIGHT:
{ "A$C157444E+3", "STRAINER" },
{ "A$C04F129AC", "CHECK_VALVE" },
```

### ❌ MISTAKE 4: Forgot to NETLOAD
```
You: Edited MyComm.cs, rebuilt it
You: Extracted JSON again
You: Still see A$ strings?

Problem: AutoCAD is running OLD DLL from memory
Solution: NETLOAD the new DLL again!
```

---

## Quick Reference: What Each Field Means

| Term | Meaning |
|------|---------|
| `A$C157444E+3` | Raw anonymous block name from AutoCAD |
| `"STRAINER"` | What you're mapping it to (semantic name) |
| Dictionary Key | The A$ name (must be exact match) |
| Dictionary Value | The semantic name (your choice) |
| NETLOAD | Command to reload DLL into AutoCAD |
| drawing_data.json | Output file with entity data |

---

## Success Criteria

You're done when:

✅ You search Notepad for `A$C157444E+3` and it says **"Cannot find"**

✅ You search Notepad for `STRAINER` and it says **"Found 1 match"**

When BOTH of these happen, the fix is working!

---

## If You Get Stuck

Check these in order:

1. **Did you save MyComm.cs after editing?** (Ctrl+S)
2. **Did you rebuild the project?** (Build → Rebuild)
3. **Did you NETLOAD the new DLL?** (Type NETLOAD, select dll)
4. **Did you extract JSON again after NETLOAD?** (extractJSON command)
5. **Are the keys spelled EXACTLY right?** (Copy-paste from JSON)

If still stuck, check the full guides:
- `DEVELOPER_IMPLEMENTATION_CHECKLIST.md` - Detailed steps
- `NOTEPAD_TEST_GUIDE.md` - Verification details
- `SOLUTION_SUMMARY.md` - Technical details

