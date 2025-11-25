# Alert Database API Documentation

This document describes the database-backed alert system with read/unread status tracking.

## Database Table: `db_alert`

The system automatically saves all alerts to the database with a limit of 200 rows. When the limit is exceeded, the oldest alerts are automatically deleted.

### Table Structure

- `id` - Primary key (auto-increment)
- `alert_id` - Unique alert identifier (GUID)
- `timestamp` - Alert creation time
- `type` - Alert type (LOG, DISCORD, ERROR, BUY, SELL, START, STOP, etc.)
- `level` - Log level (Information, Warning, Error, Debug)
- `title` - Alert title
- `message` - Alert message
- `details` - Additional details (optional)
- `fields_json` - JSON string for additional fields (optional)
- `color` - Discord color code (optional)
- `config_id` - Configuration ID (optional)
- `symbol` - Trading symbol (optional)
- `is_read` - Read status (default: false)
- `read_at` - Timestamp when marked as read (optional)

## API Endpoints

### 1. Get Alert Logs

**Endpoint:** `POST /api/Alert/GetLogs`

**Request Body:**
```json
{
  "limit": 50,
  "offset": 0,
  "type": "BUY",
  "level": "Error",
  "configId": "1",
  "isRead": false,
  "fromDate": "2024-01-01T00:00:00Z",
  "toDate": "2024-12-31T23:59:59Z"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "logs": [
      {
        "id": "guid-here",
        "timestamp": "2024-01-15T10:30:00Z",
        "type": "BUY",
        "level": "Information",
        "title": "🟢 Buy Order Executed",
        "message": "Successfully placed buy order for XRPUSDT",
        "details": null,
        "fields": {
          "Symbol": "XRPUSDT",
          "Price": "0.50000000",
          "Quantity": "100.00000000"
        },
        "color": 3447003,
        "configId": "1",
        "symbol": "XRPUSDT",
        "isRead": false,
        "readAt": null
      }
    ],
    "total": 150,
    "limit": 50,
    "offset": 0
  }
}
```

**Notes:**
- Results are ordered by newest first (timestamp DESC)
- `isRead`: `true` = only read alerts, `false` = only unread alerts, `null` = all alerts
- All other filters are optional

### 2. Get Alert by ID

**Endpoint:** `POST /api/Alert/GetAlertById`

**Request Body:**
```json
{
  "id": 123,
  "alertId": "guid-here"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "id": "guid-here",
    "timestamp": "2024-01-15T10:30:00Z",
    "type": "BUY",
    "level": "Information",
    "title": "🟢 Buy Order Executed",
    "message": "Successfully placed buy order for XRPUSDT",
    "isRead": false,
    "readAt": null
  },
  "isRead": false,
  "readAt": null
}
```

### 3. Mark Alert as Read

**Endpoint:** `POST /api/Alert/MarkAsRead`

**Request Body:**
```json
{
  "id": 123,
  "alertId": "guid-here"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Alert marked as read",
  "alertId": "guid-here"
}
```

### 4. Mark Multiple Alerts as Read

**Endpoint:** `POST /api/Alert/MarkMultipleAsRead`

**Request Body:**
```json
{
  "alertIds": ["guid-1", "guid-2", "guid-3"]
}
```

**Response:**
```json
{
  "success": true,
  "message": "3 alert(s) marked as read",
  "count": 3
}
```

### 5. Mark All Alerts as Read

**Endpoint:** `POST /api/Alert/MarkAllAsRead`

**Request Body:**
```json
{
  "configId": "1"
}
```

**Response:**
```json
{
  "success": true,
  "message": "25 alert(s) marked as read",
  "count": 25
}
```

**Notes:**
- If `configId` is provided, only marks alerts for that config as read
- If `configId` is null, marks all unread alerts as read

### 6. Get Unread Count

**Endpoint:** `POST /api/Alert/GetUnreadCount`

**Request Body:**
```json
{
  "configId": "1"
}
```

**Response:**
```json
{
  "success": true,
  "count": 15
}
```

**Notes:**
- If `configId` is provided, returns count for that config only
- If `configId` is null, returns total unread count

### 7. Clear All Logs

**Endpoint:** `POST /api/Alert/ClearLogs`

**Response:**
```json
{
  "success": true,
  "message": "All logs cleared successfully",
  "deletedCount": 200
}
```

## Usage Examples

### JavaScript/TypeScript

```javascript
// Get unread alerts
async function getUnreadAlerts() {
    const response = await fetch('http://localhost:5081/api/Alert/GetLogs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            limit: 50,
            isRead: false  // Only unread
        })
    });
    const data = await response.json();
    return data.data.logs;
}

// Mark alert as read
async function markAsRead(alertId) {
    const response = await fetch('http://localhost:5081/api/Alert/MarkAsRead', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ alertId })
    });
    return await response.json();
}

// Get unread count
async function getUnreadCount(configId) {
    const response = await fetch('http://localhost:5081/api/Alert/GetUnreadCount', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ configId })
    });
    const data = await response.json();
    return data.count;
}
```

### React Example

```jsx
import { useState, useEffect } from 'react';

function AlertList({ configId }) {
    const [alerts, setAlerts] = useState([]);
    const [unreadCount, setUnreadCount] = useState(0);

    useEffect(() => {
        loadAlerts();
        loadUnreadCount();
    }, [configId]);

    const loadAlerts = async () => {
        const response = await fetch('http://localhost:5081/api/Alert/GetLogs', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                limit: 50,
                configId: configId,
                isRead: null  // Get all
            })
        });
        const data = await response.json();
        if (data.success) {
            setAlerts(data.data.logs);
        }
    };

    const loadUnreadCount = async () => {
        const response = await fetch('http://localhost:5081/api/Alert/GetUnreadCount', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ configId })
        });
        const data = await response.json();
        if (data.success) {
            setUnreadCount(data.count);
        }
    };

    const markAsRead = async (alertId) => {
        await fetch('http://localhost:5081/api/Alert/MarkAsRead', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ alertId })
        });
        loadAlerts();
        loadUnreadCount();
    };

    const markAllAsRead = async () => {
        await fetch('http://localhost:5081/api/Alert/MarkAllAsRead', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ configId })
        });
        loadAlerts();
        loadUnreadCount();
    };

    return (
        <div>
            <div className="alert-header">
                <h2>Alerts ({unreadCount} unread)</h2>
                <button onClick={markAllAsRead}>Mark All as Read</button>
            </div>
            <div className="alert-list">
                {alerts.map(alert => (
                    <div 
                        key={alert.id} 
                        className={`alert-item ${alert.isRead ? 'read' : 'unread'}`}
                        onClick={() => !alert.isRead && markAsRead(alert.id)}
                    >
                        <div className="alert-title">{alert.title}</div>
                        <div className="alert-message">{alert.message}</div>
                        <div className="alert-time">
                            {new Date(alert.timestamp).toLocaleString()}
                        </div>
                        {!alert.isRead && <span className="unread-badge">NEW</span>}
                    </div>
                ))}
            </div>
        </div>
    );
}
```

## Features

- ✅ Automatic database persistence (200 row limit)
- ✅ Read/unread status tracking
- ✅ Ordered by newest first
- ✅ Filtering by type, level, config ID, read status, date range
- ✅ Pagination support
- ✅ Mark single, multiple, or all alerts as read
- ✅ Unread count tracking
- ✅ Real-time updates via SignalR (still works)

## Database Maintenance

- The system automatically maintains a maximum of 200 rows
- When a new alert is added and the count exceeds 200, the oldest alerts are deleted
- This ensures the database doesn't grow indefinitely
- All alerts are still broadcast via SignalR in real-time

