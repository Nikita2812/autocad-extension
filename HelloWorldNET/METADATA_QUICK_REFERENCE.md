# Quick Reference: Metadata Extraction

## What Changed?

The entity extraction now includes a **`Metadata`** field for text and block entities that organizes content and attributes in a structured way.

## Method Added

```csharp
private Dictionary<string, object> ExtractEntityMetadata(Entity entity, Transaction tr)
```

**Purpose**: Extract semantic metadata (text content, block names, attributes) from AutoCAD entities

**Parameters**:
- `entity`: The AutoCAD entity to extract metadata from
- `tr`: The active transaction (needed for block reference lookups)

**Returns**: Dictionary with metadata fields (empty if entity has no extractable metadata)

## Metadata Structure by Entity Type

### DBText Entities
```json
{
  "Metadata": {
    "type": "text",
    "content": "The actual text string",
    "height": 2.5
  }
}
```

### MText Entities
```json
{
  "Metadata": {
    "type": "mtext",
    "content": "Multi-line\ntext content",
    "height": 3.0
  }
}
```

### BlockReference Entities
```json
{
  "Metadata": {
    "type": "block",
    "block_name": "ACTUAL_BLOCK_NAME",
    "attributes": {
      "TAG1": "Value1",
      "TAG2": "Value2"
    }
  }
}
```

## Key Features

| Feature | Description |
|---------|-------------|
| **Dynamic Block Support** | Automatically resolves dynamic blocks to their actual names |
| **Safe Extraction** | Try-catch blocks prevent one bad entity from breaking extraction |
| **Cleaner Attributes** | Dictionary format instead of array for easier attribute access |
| **Consistent API** | Same extraction method used across all three extraction commands |

## Integration Points

The metadata extraction is integrated into:

1. **`ExtractDrawingJson()`** (line 48-50)
   - Interactive command with browser-based report display

2. **`ExtractDrawingJsonOnly()`** (line 1143-1145)
   - Bridge command for extraction without API call
   - Saves JSON file only

3. **`ExtractDrawingJsonSilent()`** (line 1388-1390)
   - Bridge command for extraction + API review
   - Calls API and writes result to file

## How It Works

```
Entity → ExtractEntityMetadata() → Metadata Dictionary → EntityData["Metadata"]
         ↓
      Check entity type
      ↓
   If DBText → Extract TextString, Height
   If MText → Extract Text, Height
   If BlockReference → Resolve block name + extract attributes
      ↓
   Return metadata dictionary (or empty dict)
```

## Code Pattern

```csharp
// In each extraction loop:
Dictionary<string, object> metadata = ExtractEntityMetadata(entity, tr);
if (metadata.Count > 0)
    entityData["Metadata"] = metadata;

// Followed by type-specific geometry extraction:
if (entity is DBText text)
{
    entityData["Position"] = ...;
    entityData["Rotation"] = ...;
    // Note: TextString is now in Metadata, not here
}
```

## Processing in Python

### Simple Example
```python
for entity in drawing_entities:
    if "Metadata" in entity:
        meta = entity["Metadata"]
        if meta.get("type") == "text":
            print(f"Found text: {meta['content']}")
        elif meta.get("type") == "block":
            print(f"Found block: {meta['block_name']}")
            print(f"  Attributes: {meta.get('attributes', {})}")
```

### Advanced Example
```python
# Group entities by type
text_entities = [e for e in entities 
                 if e.get("Metadata", {}).get("type") == "text"]
block_entities = [e for e in entities 
                  if e.get("Metadata", {}).get("type") == "block"]

# Extract all equipment blocks
equipment = [e for e in entities 
             if e.get("Layer") == "Equipment" and 
                e.get("Metadata", {}).get("type") == "block"]

for equip in equipment:
    block_name = equip["Metadata"]["block_name"]
    attrs = equip["Metadata"].get("attributes", {})
    # Process equipment...
```

## Backward Compatibility

✅ **Existing geometry fields preserved**
- Position, Rotation, Scale still present
- Line/Circle/Arc/Polyline extractions unchanged

⚠️ **Content moved to Metadata**
- Old code looking for `entityData["Content"]` should now use `entityData["Metadata"]["content"]`
- Old code looking for `entityData["BlockName"]` should now use `entityData["Metadata"]["block_name"]`
- Old code looking for `entityData["Attributes"]` (array) should now use `entityData["Metadata"]["attributes"]` (dict)

## Testing Checklist

- [ ] Extract drawing with DBText entities (TextCallout layer)
- [ ] Extract drawing with MText entities
- [ ] Extract drawing with BlockReferences with attributes
- [ ] Extract drawing with Dynamic Blocks
- [ ] Verify JSON contains `Metadata` field
- [ ] Verify block names are resolved correctly
- [ ] Verify attributes are in dictionary format
- [ ] Verify empty `Metadata` is not added for non-text/block entities

## Common Errors & Solutions

| Error | Cause | Solution |
|-------|-------|----------|
| Metadata field missing | Entity type doesn't have metadata | Check entity type (only text/block) |
| "block_name" is null | Dynamic block lookup failed | Verify block is accessible in transaction |
| "attributes" missing | Block has no attributes | Check if block actually has attributes |
| "content" is empty | Text string is empty | This is valid - text can be empty |

## References

- **Full Documentation**: See `METADATA_EXTRACTION_CHANGES.md`
- **Examples**: See `METADATA_EXTRACTION_EXAMPLES.md`
- **Source Code**: `MyComm.cs` lines 911-972 (ExtractEntityMetadata method)
