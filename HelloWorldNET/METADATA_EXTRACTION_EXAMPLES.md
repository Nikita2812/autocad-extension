# Metadata Extraction Implementation Examples

## Overview

This document provides practical examples of how the enhanced metadata extraction works with different entity types.

## Example 1: TextCallout with DBText

### AutoCAD Drawing
- Entity Type: DBText
- Content: "Section A-A"
- Layer: "TextCallout"
- Position: (150, 200, 0)
- Height: 3.5
- Rotation: 0.0

### Extracted JSON
```json
{
  "Type": "DBText",
  "Layer": "TextCallout",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {
    "X": 150.0,
    "Y": 200.0,
    "Z": 0.0
  },
  "Rotation": 0.0,
  "Metadata": {
    "type": "text",
    "content": "Section A-A",
    "height": 3.5
  }
}
```

### Python Processing Example
```python
for entity in entities:
    if entity["Type"] == "DBText" and "Metadata" in entity:
        metadata = entity["Metadata"]
        if metadata["type"] == "text":
            text_content = metadata["content"]
            print(f"Text: {text_content}")
```

---

## Example 2: Equipment Block with Attributes

### AutoCAD Drawing
- Entity Type: BlockReference
- Block Name: "EQUIP_PUMP"
- Layer: "Equipment"
- Position: (500, 300, 0)
- Rotation: 45.0 degrees
- Attributes:
  - TagNumber: "P-101A"
  - Description: "Centrifugal Pump"
  - Capacity: "50 GPM"

### Extracted JSON
```json
{
  "Type": "BlockReference",
  "Layer": "Equipment",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {
    "X": 500.0,
    "Y": 300.0,
    "Z": 0.0
  },
  "Rotation": 0.7853981633974483,
  "ScaleX": 1.0,
  "ScaleY": 1.0,
  "ScaleZ": 1.0,
  "Metadata": {
    "type": "block",
    "block_name": "EQUIP_PUMP",
    "attributes": {
      "TagNumber": "P-101A",
      "Description": "Centrifugal Pump",
      "Capacity": "50 GPM"
    }
  }
}
```

### Python Processing Example
```python
for entity in entities:
    if entity["Type"] == "BlockReference" and "Metadata" in entity:
        metadata = entity["Metadata"]
        block_name = metadata["block_name"]
        attributes = metadata.get("attributes", {})
        
        tag_number = attributes.get("TagNumber", "N/A")
        description = attributes.get("Description", "N/A")
        
        print(f"Block: {block_name}, Tag: {tag_number}, Desc: {description}")
```

---

## Example 3: Multi-line Text (MText)

### AutoCAD Drawing
- Entity Type: MText
- Content: "Designed per ASME B31.3\nMaterial: Carbon Steel\nPressure: 150 PSI"
- Layer: "Annotations"
- Position: (250, 450, 0)
- Height: 2.5

### Extracted JSON
```json
{
  "Type": "MText",
  "Layer": "Annotations",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {
    "X": 250.0,
    "Y": 450.0,
    "Z": 0.0
  },
  "Metadata": {
    "type": "mtext",
    "content": "Designed per ASME B31.3\nMaterial: Carbon Steel\nPressure: 150 PSI",
    "height": 2.5
  }
}
```

### Python Processing Example
```python
for entity in entities:
    if entity["Type"] == "MText" and "Metadata" in entity:
        metadata = entity["Metadata"]
        lines = metadata["content"].split("\n")
        for line in lines:
            print(f"  {line}")
```

---

## Example 4: Dynamic Block with Attributes

### AutoCAD Drawing
- Entity Type: BlockReference (Dynamic)
- Block Name: `Anonymous Block (resolves to "VALVE_GATE")`
- Layer: "Piping"
- Attributes:
  - SerialNo: "V-45-23"
  - Size: "2-inch"
  - Type: "Gate Valve"

### Extracted JSON
```json
{
  "Type": "BlockReference",
  "Layer": "Piping",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {
    "X": 350.0,
    "Y": 275.0,
    "Z": 0.0
  },
  "Rotation": 1.5707963267948966,
  "ScaleX": 1.0,
  "ScaleY": 1.0,
  "ScaleZ": 1.0,
  "Metadata": {
    "type": "block",
    "block_name": "VALVE_GATE",
    "attributes": {
      "SerialNo": "V-45-23",
      "Size": "2-inch",
      "Type": "Gate Valve"
    }
  }
}
```

**Note:** The metadata correctly shows `"block_name": "VALVE_GATE"` even though the block reference might internally reference an anonymous block. The extraction logic automatically resolves dynamic blocks to their actual names.

### Python Processing Example
```python
for entity in entities:
    if entity["Type"] == "BlockReference":
        metadata = entity["Metadata"]
        
        # Correctly gets the resolved dynamic block name
        actual_block_name = metadata["block_name"]
        
        # Safe attribute access
        attributes = metadata.get("attributes", {})
        serial = attributes.get("SerialNo", "Unknown")
        
        print(f"Valve: {actual_block_name}, Serial: {serial}")
```

---

## Example 5: Block Without Attributes

### AutoCAD Drawing
- Entity Type: BlockReference
- Block Name: "NORTH_ARROW"
- Layer: "Symbols"
- No attributes defined

### Extracted JSON
```json
{
  "Type": "BlockReference",
  "Layer": "Symbols",
  "Color": "256",
  "Linetype": "Continuous",
  "Position": {
    "X": 600.0,
    "Y": 750.0,
    "Z": 0.0
  },
  "Rotation": 0.0,
  "ScaleX": 1.0,
  "ScaleY": 1.0,
  "ScaleZ": 1.0,
  "Metadata": {
    "type": "block",
    "block_name": "NORTH_ARROW"
  }
}
```

**Note:** The `"attributes"` field is not included when there are no attributes, keeping the JSON clean.

---

## Querying Metadata in Python

### Helper Function
```python
def get_entity_metadata(entity):
    """Safely extract metadata from entity JSON."""
    return entity.get("Metadata", {})

def get_block_attributes(entity):
    """Get attributes dictionary for a block entity."""
    metadata = get_entity_metadata(entity)
    return metadata.get("attributes", {})

def get_text_content(entity):
    """Get text content from text entity."""
    metadata = get_entity_metadata(entity)
    return metadata.get("content", "")

# Usage
for entity in entities:
    metadata = get_entity_metadata(entity)
    
    if metadata.get("type") == "text":
        print(f"Text: {get_text_content(entity)}")
    
    elif metadata.get("type") == "block":
        block_name = metadata.get("block_name", "Unknown")
        attrs = get_block_attributes(entity)
        print(f"Block: {block_name}, Attributes: {attrs}")
```

---

## Summary Table

| Entity Type | Metadata Type | Key Fields | Use Case |
|-------------|---------------|-----------|----------|
| DBText | `"text"` | `content`, `height` | Text callouts, labels |
| MText | `"mtext"` | `content`, `height` | Multi-line notes, descriptions |
| BlockReference | `"block"` | `block_name`, `attributes` | Equipment, symbols, components |
| BlockReference (Dynamic) | `"block"` | `block_name` (resolved), `attributes` | Variable blocks, dynamic content |

---

## Benefits of Structured Metadata

1. **Consistent Access**: All text/block data accessed through `Metadata` object
2. **Type Safety**: `metadata["type"]` tells you what kind of entity you have
3. **No Duplication**: Content/block info not repeated in main entity fields
4. **Cleaner JSON**: Geometry stays separate from semantic metadata
5. **Easy Filtering**: Can quickly find all text or all blocks with attributes
6. **Future Extensibility**: Easy to add more metadata fields without breaking existing code
