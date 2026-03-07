# extractJSONOnly Command Documentation

## Overview
The `extractJSONOnly` command extracts drawing entities to JSON format **without calling the API**. It's designed for Python bridge automation, reading input from a config file and writing results to a result file.

## Usage

### 1. Write Request Config
Create `%TEMP%\ai_review\extract_request.json`:

```json
{
  "drawing_path": "C:\\path\\to\\drawing.dwg",
  "uuid": "123e4567-e89b-12d3-a456-426614174000"
}
```

**Parameters:**
- `drawing_path` (required): Full path to the DWG or DXF file
- `uuid` (optional): Unique identifier for the extraction. If provided, the output file will be named `drawing_data_{uuid}.json`

### 2. Run the Command in AutoCAD
```
extractJSONOnly
```

### 3. Read Result
The result will be written to `%TEMP%\ai_review\extract_result.json`:

**Success Response:**
```json
{
  "success": true,
  "message": "Extracted 42 entities.",
  "timestamp": "2024-01-15T10:30:45Z",
  "json_path": "C:\\path\\to\\drawing_data_123e4567-e89b-12d3-a456-426614174000.json",
  "json_content": [
    {
      "Type": "Line",
      "Layer": "0",
      "Color": "256",
      "Linetype": "Continuous",
      "StartPoint": {"X": 0.0, "Y": 0.0, "Z": 0.0},
      "EndPoint": {"X": 10.0, "Y": 20.0, "Z": 0.0}
    },
    ...
  ]
}
```

**Error Response:**
```json
{
  "success": false,
  "message": "Drawing file not found: C:\\path\\to\\missing.dwg",
  "timestamp": "2024-01-15T10:30:45Z",
  "json_path": null,
  "json_content": null
}
```

## Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `success` | boolean | Whether extraction was successful |
| `message` | string | Status message or error description |
| `timestamp` | string | UTC timestamp of execution (ISO 8601 format) |
| `json_path` | string | Full path to the saved JSON file (null if failed) |
| `json_content` | object | The extracted JSON content (null if failed) |

## File Paths (Hardcoded in Plugin)

| Purpose | Path |
|---------|------|
| Request Config | `%TEMP%\ai_review\extract_request.json` |
| Result Output | `%TEMP%\ai_review\extract_result.json` |

## Extracted Entity Fields

For each entity, the following properties are extracted:

**All Entities:**
- `Type`: Entity type name (Line, Circle, Arc, Polyline, etc.)
- `Layer`: Layer name
- `Color`: Color index as string
- `Linetype`: Linetype name

**Lines:**
- `StartPoint`: {X, Y, Z} coordinates
- `EndPoint`: {X, Y, Z} coordinates

**Circles:**
- `Center`: {X, Y, Z} coordinates
- `Radius`: Circle radius

**Arcs:**
- `Center`: {X, Y, Z} coordinates
- `Radius`: Arc radius
- `StartAngle`: Start angle in radians
- `EndAngle`: End angle in radians

**Polylines:**
- `Vertices`: Array of {X, Y, Z} coordinates for each vertex

## Error Handling

Common error scenarios:

| Error | Cause | Resolution |
|-------|-------|-----------|
| `Request config file not found.` | `extract_request.json` doesn't exist | Create the file at `%TEMP%\ai_review\extract_request.json` |
| `drawing_path not provided in request.` | Missing `drawing_path` parameter | Add `drawing_path` to the request JSON |
| `Drawing file not found: ...` | File path is invalid or file doesn't exist | Verify the file path exists and is accessible |
| `Failed to extract from drawing: ...` | Error opening/reading the drawing | Check file isn't corrupted or locked by another process |

## Python Bridge Example

```python
import json
import os
import time
from pathlib import Path

def extract_drawing_json(drawing_path: str, uuid: str = None) -> dict:
    """Extract JSON from a drawing using AutoCAD plugin."""
    
    temp_dir = Path(os.environ['TEMP']) / 'ai_review'
    temp_dir.mkdir(parents=True, exist_ok=True)
    
    request_file = temp_dir / 'extract_request.json'
    result_file = temp_dir / 'extract_result.json'
    
    # Write request
    request_data = {
        "drawing_path": str(drawing_path),
        "uuid": uuid
    }
    
    with open(request_file, 'w') as f:
        json.dump(request_data, f)
    
    # Run command in AutoCAD (via bridge)
    # ... call AutoCAD with extractJSONOnly command ...
    
    # Poll for result
    max_wait = 60  # seconds
    start_time = time.time()
    
    while time.time() - start_time < max_wait:
        if result_file.exists():
            with open(result_file, 'r', encoding='utf-8-sig') as f:
                result = json.load(f)
            
            if result.get('success'):
                return {
                    'success': True,
                    'json_path': result['json_path'],
                    'json_content': result['json_content']
                }
            else:
                return {
                    'success': False,
                    'error': result['message']
                }
        
        time.sleep(0.5)
    
    return {'success': False, 'error': 'Extraction timeout'}
```

## Related Commands

- `extractJSON` - Interactive command with browser report
- `extractJSONSilent` - Extract + API call + report formatting
- `extractJSONOnly` - Extract only (no API call) ← **This command**
