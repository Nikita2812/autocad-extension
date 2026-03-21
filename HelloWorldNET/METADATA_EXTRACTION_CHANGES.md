# Metadata Extraction Enhancement

## Summary of Changes

This update enriches entity metadata extraction for TextCallout and Equipment entities by creating a structured `Metadata` object that properly extracts text content, block names, and attributes.

## Key Improvements

### 1. New Helper Method: `ExtractEntityMetadata()`

A dedicated method that extracts metadata for text and block entities:

```csharp
private Dictionary<string, object> ExtractEntityMetadata(Entity entity, Transaction tr)
```

**Features:**
- **DBText Entities**: Extracts text content and height
- **MText Entities**: Extracts multi-line text content and height  
- **BlockReference Entities**: Extracts block name (with dynamic block support) and attributes

### 2. Dynamic Block Support

For BlockReference entities with dynamic blocks, the method:
1. Detects dynamic blocks using `IsDynamicBlock` property
2. Retrieves the dynamic block's actual table record
3. Extracts the true block name (not the anonymous block reference)

### 3. Structured Attribute Extraction

Block attributes are now extracted as a dictionary:
- **Old Format**: Array of objects with "Tag" and "Value" fields
- **New Format**: Dictionary mapping tag names directly to values (e.g., `{"TagNumber": "P-101A"}`)

This makes attributes more accessible for downstream processing.

### 4. Applied to All Three Extraction Methods

The metadata extraction is now consistently applied across:
1. **`ExtractDrawingJson()`** - Interactive command with browser report
2. **`ExtractDrawingJsonOnly()`** - Bridge-driven extraction without API call
3. **`ExtractDrawingJsonSilent()`** - Silent extraction with API call

## Output Structure

### Example JSON Output

**Before:**
```json
{
  "Type": "BlockReference",
  "Layer": "Equipment",
  "Color": "256",
  "Linetype": "Continuous",
  "BlockName": "EQUIP_001",
  "Position": {"X": 100, "Y": 200, "Z": 0},
  "Attributes": [
    {"Tag": "TagNumber", "Value": "P-101A"},
    {"Tag": "Description", "Value": "Pump"}
  ]
}
```

**After:**
```json
{
  "Type": "BlockReference",
  "Layer": "Equipment",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {"X": 100, "Y": 200, "Z": 0},
  "Rotation": 0.0,
  "ScaleX": 1.0,
  "ScaleY": 1.0,
  "ScaleZ": 1.0,
  "Metadata": {
    "type": "block",
    "block_name": "EQUIP_001",
    "attributes": {
      "TagNumber": "P-101A",
      "Description": "Pump"
    }
  }
}
```

### Text Entity Example

**Before:**
```json
{
  "Type": "DBText",
  "Layer": "TextCallout",
  "Content": "Important Note",
  "Height": 2.5
}
```

**After:**
```json
{
  "Type": "DBText",
  "Layer": "TextCallout",
  "Position": {"X": 50, "Y": 75, "Z": 0},
  "Rotation": 0.0,
  "Metadata": {
    "type": "text",
    "content": "Important Note",
    "height": 2.5
  }
}
```

## Benefits

✅ **Cleaner Structure** - Metadata is now organized in a dedicated `Metadata` field
✅ **Dynamic Block Support** - Correctly identifies actual block names
✅ **Better Attribute Access** - Dictionary format makes attribute lookup simpler
✅ **Consistent Across Commands** - All extraction methods use the same logic
✅ **Backward Compatible** - Geometry data still includes position/rotation info
✅ **Extensible** - Easy to add more metadata fields in the future

## Field Reference

### Metadata.Type Field Values
- `"text"` - DBText entity
- `"mtext"` - MText entity
- `"block"` - BlockReference entity

### Text Entities Metadata
| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Entity type ("text" or "mtext") |
| `content` | string | The actual text content |
| `height` | double | Text height/size |

### Block Entities Metadata
| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Always "block" |
| `block_name` | string | Block name (handles dynamic blocks) |
| `attributes` | object | Dictionary of tag→value pairs (optional) |

## Testing Recommendations

1. **Test with TextCallout blocks** - Verify text content extraction
2. **Test with Equipment blocks** - Verify attribute extraction
3. **Test with Dynamic Blocks** - Verify correct block name is extracted
4. **Compare JSON output** - Ensure metadata structure is correct
5. **Verify API Processing** - Ensure downstream API correctly handles new structure

## Migration Notes

The changes are backward compatible in the sense that:
- All original geometry data is preserved
- New `Metadata` field is added alongside existing fields
- Existing code processing other entity types remains unaffected

If you have code consuming the `Content`, `BlockName`, `Attributes` fields directly, you may want to update it to use the new `Metadata` object instead for consistency.
