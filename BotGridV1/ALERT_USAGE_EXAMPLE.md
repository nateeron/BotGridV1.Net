# Alert & Log System Usage Guide

This document explains how to use the Alert/Log webhook system to display all logs and Discord messages in the UI.

## Overview

The system provides:
- **Real-time alerts** via SignalR for immediate UI updates
- **Historical logs** via REST API for pagination and filtering
- **Automatic capture** of all Discord messages and application logs

## SignalR Connection (Real-time Alerts)

### JavaScript/TypeScript Example

```javascript
import * as signalR from "@microsoft/signalr";

// Create connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5081/hubs/alerts", {
        withCredentials: true
    })
    .withAutomaticReconnect()
    .build();

// Start connection
await connection.start();

// Join group for specific config ID (optional)
await connection.invoke("JoinAlertGroup", "1");

// Or join all alerts
await connection.invoke("JoinAllAlerts");

// Listen for new alerts
connection.on("NewAlert", (alert) => {
    console.log("New alert received:", alert);
    
    // Update UI
    displayAlert(alert);
});

// Handle connection events
connection.onreconnecting(() => {
    console.log("Reconnecting...");
});

connection.onreconnected(() => {
    console.log("Reconnected!");
});

connection.onclose(() => {
    console.log("Connection closed");
});
```

### React Example

```jsx
import { useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';

function AlertPanel({ configId }) {
    const [alerts, setAlerts] = useState([]);
    const [connection, setConnection] = useState(null);

    useEffect(() => {
        const newConnection = new signalR.HubConnectionBuilder()
            .withUrl('http://localhost:5081/hubs/alerts', {
                withCredentials: true
            })
            .withAutomaticReconnect()
            .build();

        newConnection.start()
            .then(() => {
                console.log('SignalR Connected');
                
                // Join config-specific group
                if (configId) {
                    newConnection.invoke('JoinAlertGroup', configId.toString());
                } else {
                    newConnection.invoke('JoinAllAlerts');
                }
            })
            .catch(err => console.error('SignalR Connection Error:', err));

        // Listen for new alerts
        newConnection.on('NewAlert', (alert) => {
            setAlerts(prev => [alert, ...prev].slice(0, 100)); // Keep last 100
        });

        setConnection(newConnection);

        return () => {
            newConnection.stop();
        };
    }, [configId]);

    return (
        <div className="alert-panel">
            <h2>Alerts & Logs</h2>
            <div className="alert-list">
                {alerts.map(alert => (
                    <AlertCard key={alert.id} alert={alert} />
                ))}
            </div>
        </div>
    );
}

function AlertCard({ alert }) {
    const getColor = () => {
        if (alert.color) {
            return `#${alert.color.toString(16).padStart(6, '0')}`;
        }
        return '#3498db';
    };

    return (
        <div 
            className="alert-card" 
            style={{ borderLeft: `4px solid ${getColor()}` }}
        >
            <div className="alert-header">
                <span className="alert-type">{alert.type}</span>
                <span className="alert-level">{alert.level}</span>
                <span className="alert-time">
                    {new Date(alert.timestamp).toLocaleString()}
                </span>
            </div>
            <div className="alert-title">{alert.title}</div>
            <div className="alert-message">{alert.message}</div>
            {alert.details && (
                <div className="alert-details">{alert.details}</div>
            )}
            {alert.fields && (
                <div className="alert-fields">
                    {Object.entries(alert.fields).map(([key, value]) => (
                        <div key={key} className="field">
                            <strong>{key}:</strong> {value}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
```

## REST API Endpoints

### Get Alert Logs

```javascript
// POST /api/Alert/GetLogs
const response = await fetch('http://localhost:5081/api/Alert/GetLogs', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json'
    },
    body: JSON.stringify({
        limit: 50,
        offset: 0,
        type: 'BUY',           // Optional: filter by type
        level: 'Error',         // Optional: filter by level
        configId: '1',          // Optional: filter by config ID
        fromDate: '2024-01-01T00:00:00Z',  // Optional
        toDate: '2024-12-31T23:59:59Z'     // Optional
    })
});

const data = await response.json();
console.log(data.data.logs);    // Array of alerts
console.log(data.data.total);   // Total count
```

### Get Statistics

```javascript
// POST /api/Alert/GetStatistics
const response = await fetch('http://localhost:5081/api/Alert/GetStatistics', {
    method: 'POST'
});

const data = await response.json();
console.log(data.data);
// {
//   "Total": 150,
//   "Errors": 5,
//   "Warnings": 10,
//   "Information": 120,
//   "Discord": 50,
//   "Buy": 30,
//   "Sell": 25
// }
```

### Clear Logs

```javascript
// POST /api/Alert/ClearLogs
const response = await fetch('http://localhost:5081/api/Alert/ClearLogs', {
    method: 'POST'
});

const data = await response.json();
console.log(data.message); // "All logs cleared successfully"
```

## Alert Types

The system captures the following alert types:

- **LOG** - Application logs (Warning, Error level)
- **DISCORD** - Discord webhook messages
- **ERROR** - Error messages
- **BUY** - Buy order executed
- **SELL** - Sell order executed
- **START** - Bot started
- **STOP** - Bot stopped
- **BUY_RETRY** - Buy retry attempts
- **BUY_FAILED** - Buy order failures

## Alert Structure

```typescript
interface AlertLog {
    id: string;                    // Unique ID
    timestamp: string;              // ISO 8601 timestamp
    type: string;                   // Alert type
    level: string;                  // Log level (Information, Warning, Error)
    title: string;                  // Alert title
    message: string;               // Alert message
    details?: string;              // Additional details
    fields?: { [key: string]: string };  // Key-value fields
    color?: number;                 // Discord color code (hex)
    configId?: string;             // Configuration ID
    symbol?: string;                // Trading symbol
}
```

## Complete React Component Example

```jsx
import { useEffect, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

export default function AlertSystem() {
    const [alerts, setAlerts] = useState([]);
    const [connection, setConnection] = useState(null);
    const [stats, setStats] = useState({});

    // Load initial alerts
    useEffect(() => {
        loadAlerts();
        loadStatistics();
    }, []);

    // Setup SignalR connection
    useEffect(() => {
        const conn = new signalR.HubConnectionBuilder()
            .withUrl('http://localhost:5081/hubs/alerts', {
                withCredentials: true
            })
            .withAutomaticReconnect()
            .build();

        conn.start()
            .then(() => {
                conn.invoke('JoinAllAlerts');
            })
            .catch(console.error);

        conn.on('NewAlert', (alert) => {
            setAlerts(prev => [alert, ...prev].slice(0, 200));
            loadStatistics(); // Refresh stats
        });

        setConnection(conn);

        return () => conn.stop();
    }, []);

    const loadAlerts = async () => {
        try {
            const res = await fetch('http://localhost:5081/api/Alert/GetLogs', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ limit: 100 })
            });
            const data = await res.json();
            if (data.success) {
                setAlerts(data.data.logs);
            }
        } catch (error) {
            console.error('Failed to load alerts:', error);
        }
    };

    const loadStatistics = async () => {
        try {
            const res = await fetch('http://localhost:5081/api/Alert/GetStatistics', {
                method: 'POST'
            });
            const data = await res.json();
            if (data.success) {
                setStats(data.data);
            }
        } catch (error) {
            console.error('Failed to load statistics:', error);
        }
    };

    const clearLogs = async () => {
        if (!confirm('Clear all logs?')) return;
        
        try {
            const res = await fetch('http://localhost:5081/api/Alert/ClearLogs', {
                method: 'POST'
            });
            const data = await res.json();
            if (data.success) {
                setAlerts([]);
                setStats({});
            }
        } catch (error) {
            console.error('Failed to clear logs:', error);
        }
    };

    return (
        <div className="alert-system">
            <div className="alert-header">
                <h2>Alerts & Logs</h2>
                <div className="stats">
                    <span>Total: {stats.Total || 0}</span>
                    <span>Errors: {stats.Errors || 0}</span>
                    <span>Warnings: {stats.Warnings || 0}</span>
                </div>
                <button onClick={clearLogs}>Clear Logs</button>
            </div>
            
            <div className="alert-list">
                {alerts.map(alert => (
                    <AlertCard key={alert.id} alert={alert} />
                ))}
            </div>
        </div>
    );
}
```

## CSS Styling Example

```css
.alert-system {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}

.alert-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 20px;
}

.alert-list {
    display: flex;
    flex-direction: column;
    gap: 10px;
    max-height: 600px;
    overflow-y: auto;
}

.alert-card {
    background: #fff;
    border-radius: 8px;
    padding: 15px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    border-left: 4px solid #3498db;
}

.alert-header {
    display: flex;
    gap: 10px;
    margin-bottom: 10px;
    font-size: 0.9em;
    color: #666;
}

.alert-title {
    font-weight: bold;
    font-size: 1.1em;
    margin-bottom: 5px;
}

.alert-message {
    margin-bottom: 10px;
}

.alert-fields {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 5px;
    margin-top: 10px;
    font-size: 0.9em;
}
```

## Notes

- The system keeps the last 1000 logs in memory
- Real-time alerts are sent via SignalR immediately
- Historical logs can be retrieved via REST API with filtering
- All Discord messages are automatically captured and sent to UI
- Application logs (Warning and Error level) are automatically captured

