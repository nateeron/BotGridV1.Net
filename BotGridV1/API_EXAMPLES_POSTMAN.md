# Postman API Examples - Login & Signup

## Base URL
```
http://localhost:5000
```
or
```
https://localhost:5001
```
*(Check your application's launchSettings.json for the exact port)*

---

## 1. Signup API

### Request Details
- **Method:** `POST`
- **URL:** `http://localhost:5000/api/Login/Signup`
- **Headers:**
  ```
  Content-Type: application/json
  ```

### Request Body (JSON)
```json
{
  "username": "john_doe",
  "password": "SecurePassword123!",
  "email": "john.doe@example.com",
  "fullName": "John Doe",
  "phoneNumber": "+1234567890"
}
```

### Minimal Request Body (Required Fields Only)
```json
{
  "username": "testuser",
  "password": "Test123456"
}
```

### Example cURL
```bash
curl -X POST "http://localhost:5000/api/Login/Signup" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "john_doe",
    "password": "SecurePassword123!",
    "email": "john.doe@example.com",
    "fullName": "John Doe",
    "phoneNumber": "+1234567890"
  }'
```

### Success Response (200 OK)
```json
{
  "userID": 1,
  "username": "john_doe",
  "message": "User registered successfully."
}
```

### Error Responses

**400 Bad Request - Username Already Exists**
```json
"Username already exists"
```

**400 Bad Request - Email Already Exists**
```json
"Email already exists"
```

**400 Bad Request - Missing Required Fields**
```json
"Username and password are required"
```

---

## 2. Login API

### Request Details
- **Method:** `POST`
- **URL:** `http://localhost:5000/api/Login/Login`
- **Headers:**
  ```
  Content-Type: application/json
  ```

### Request Body (JSON)
```json
{
  "username": "john_doe",
  "password": "SecurePassword123!"
}
```

### Example cURL
```bash
curl -X POST "http://localhost:5000/api/Login/Login" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "john_doe",
    "password": "SecurePassword123!"
  }'
```

### Success Response (200 OK)
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2huX2RvZSIsInJvbGUiOiJVc2VyIiwidXNlcklkIjoiMSIsImV4cCI6MTY4OTU2NzIwMCwiaXNzIjoieW91cklzc3VlciIsImF1ZCI6InlvdXJBdWRpZW5jZSJ9...",
  "expireAt": "2024-07-15T10:30:00"
}
```

### Error Responses

**401 Unauthorized - Invalid Credentials**
```json
"Invalid username or password"
```

**401 Unauthorized - Account Inactive**
```json
"Account is inactive"
```

**401 Unauthorized - Account Locked**
```json
"Account is locked. Please contact administrator."
```

**400 Bad Request - Missing Fields**
```json
"Username and password are required"
```

---

## 3. Using the Token (Authentication)

After successful login, use the JWT token in subsequent requests:

### Example: Get User Profile

**Request Details:**
- **Method:** `GET`
- **URL:** `http://localhost:5000/api/Login/Profile`
- **Headers:**
  ```
  Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
  Content-Type: application/json
  ```

### Example cURL
```bash
curl -X GET "http://localhost:5000/api/Login/Profile" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  -H "Content-Type: application/json"
```

---

## Postman Collection Setup

### 1. Create New Collection
1. Open Postman
2. Click "New" → "Collection"
3. Name it "BotGrid API"

### 2. Add Signup Request
1. Click "Add Request" in the collection
2. Name it "Signup"
3. Set Method to `POST`
4. Enter URL: `http://localhost:5000/api/Login/Signup`
5. Go to "Body" tab → Select "raw" → Choose "JSON"
6. Paste the request body:
```json
{
  "username": "testuser",
  "password": "Test123456",
  "email": "test@example.com",
  "fullName": "Test User",
  "phoneNumber": "+1234567890"
}
```
7. Click "Send"

### 3. Add Login Request
1. Click "Add Request" in the collection
2. Name it "Login"
3. Set Method to `POST`
4. Enter URL: `http://localhost:5000/api/Login/Login`
5. Go to "Body" tab → Select "raw" → Choose "JSON"
6. Paste the request body:
```json
{
  "username": "testuser",
  "password": "Test123456"
}
```
7. Click "Send"
8. Copy the `token` from the response

### 4. Add Profile Request (Authenticated)
1. Click "Add Request" in the collection
2. Name it "Get Profile"
3. Set Method to `GET`
4. Enter URL: `http://localhost:5000/api/Login/Profile`
5. Go to "Authorization" tab
6. Select Type: "Bearer Token"
7. Paste the token from Login response
8. Click "Send"

---

## Postman Environment Variables (Recommended)

### Create Environment
1. Click "Environments" → "Create Environment"
2. Name it "Local Development"
3. Add variables:
   - `baseUrl`: `http://localhost:5000`
   - `token`: (leave empty, will be set after login)

### Update URLs to Use Variables
- Signup URL: `{{baseUrl}}/api/Login/Signup`
- Login URL: `{{baseUrl}}/api/Login/Login`
- Profile URL: `{{baseUrl}}/api/Login/Profile`

### Auto-save Token After Login
1. In the Login request, go to "Tests" tab
2. Add this script:
```javascript
if (pm.response.code === 200) {
    var jsonData = pm.response.json();
    pm.environment.set("token", jsonData.token);
    console.log("Token saved to environment");
}
```

3. In Profile request Authorization, use: `{{token}}`

---

## Field Descriptions

### SignupRequest Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `username` | string | Yes | Unique username (max 100 chars) |
| `password` | string | Yes | User password (will be hashed) |
| `email` | string | No | Email address (must be unique if provided) |
| `fullName` | string | No | User's full name (max 200 chars) |
| `phoneNumber` | string | No | Phone number (max 50 chars) |
| `configID` | int | No | Configuration ID (deprecated, not used) |

### LoginRequest Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `username` | string | Yes | Username |
| `password` | string | Yes | User password |

### LoginResponse Fields

| Field | Type | Description |
|-------|------|-------------|
| `token` | string | JWT authentication token |
| `expireAt` | datetime | Token expiration time |

---

## Security Notes

1. **Account Locking**: After 5 failed login attempts, the account will be locked
2. **Password Hashing**: Passwords are hashed using BCrypt (never stored in plain text)
3. **Token Expiration**: JWT tokens expire after 60 minutes (default)
4. **HTTPS Recommended**: Always use HTTPS in production

---

## Testing Workflow

1. **First, create a user:**
   - Use Signup API with unique username and email

2. **Then, login:**
   - Use Login API with the created credentials
   - Save the returned token

3. **Access protected resources:**
   - Use the token in Authorization header for authenticated endpoints

---

## Report API - Get Order Report

### Request Details
- **Method:** `POST`
- **URL:** `http://localhost:5081/api/Report/GetOrderReport`
- **Headers:**
  ```
  Content-Type: application/json
  ```

### Request Body
No request body required. Send empty JSON object:
```json
{}
```

### Example cURL
```bash
curl -X POST "http://localhost:5081/api/Report/GetOrderReport" \
  -H "Content-Type: application/json" \
  -d '{}'
```

### Example JavaScript/Fetch
```javascript
fetch('http://localhost:5081/api/Report/GetOrderReport', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({})
})
  .then(response => response.json())
  .then(data => {
    console.log('Total Orders:', data.data.totalOrderAll);
    console.log('Sold Orders:', data.data.orderSold);
    console.log('Waiting Orders:', data.data.orderWaiting);
    console.log('Profit/Loss USDT:', data.data.soldProfitLoss.usdt);
    console.log('Profit/Loss THB:', data.data.soldProfitLoss.thb);
    console.log('Exchange Rate:', data.data.soldProfitLoss.exchangeRate);
  })
  .catch(error => console.error('Error:', error));
```

### Example C# HttpClient
```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;

var client = new HttpClient();
client.BaseAddress = new Uri("http://localhost:5081/");

var response = await client.PostAsync("api/Report/GetOrderReport", 
    new StringContent("{}", Encoding.UTF8, "application/json"));

if (response.IsSuccessStatusCode)
{
    var jsonString = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<OrderReportResponse>(jsonString, 
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    
    Console.WriteLine($"Total Orders: {result.Data.TotalOrderAll}");
    Console.WriteLine($"Sold Orders: {result.Data.OrderSold}");
    Console.WriteLine($"Waiting Orders: {result.Data.OrderWaiting}");
    Console.WriteLine($"Profit/Loss USDT: {result.Data.SoldProfitLoss.Usdt}");
    Console.WriteLine($"Profit/Loss THB: {result.Data.SoldProfitLoss.Thb}");
    Console.WriteLine($"Exchange Rate: {result.Data.SoldProfitLoss.ExchangeRate}");
}

public class OrderReportResponse
{
    public bool Success { get; set; }
    public OrderReportData Data { get; set; }
}

public class OrderReportData
{
    public int TotalOrderAll { get; set; }
    public int OrderSold { get; set; }
    public int OrderWaiting { get; set; }
    public ProfitLossData SoldProfitLoss { get; set; }
}

public class ProfitLossData
{
    public decimal Usdt { get; set; }
    public decimal Thb { get; set; }
    public decimal ExchangeRate { get; set; }
}
```

### Example Python requests
```python
import requests

url = "http://localhost:5081/api/Report/GetOrderReport"
headers = {"Content-Type": "application/json"}
data = {}

response = requests.post(url, headers=headers, json=data)

if response.status_code == 200:
    result = response.json()
    print(f"Total Orders: {result['data']['totalOrderAll']}")
    print(f"Sold Orders: {result['data']['orderSold']}")
    print(f"Waiting Orders: {result['data']['orderWaiting']}")
    print(f"Profit/Loss USDT: {result['data']['soldProfitLoss']['usdt']}")
    print(f"Profit/Loss THB: {result['data']['soldProfitLoss']['thb']}")
    print(f"Exchange Rate: {result['data']['soldProfitLoss']['exchangeRate']}")
else:
    print(f"Error: {response.status_code} - {response.text}")
```

### Success Response (200 OK)
```json
{
  "success": true,
  "data": {
    "totalOrderAll": 150,
    "orderSold": 120,
    "orderWaiting": 25,
    "soldProfitLoss": {
      "usdt": 1234.56,
      "thb": 43209.60,
      "exchangeRate": 35.0000
    }
  }
}
```

### Response Fields Explanation
- `success`: Boolean indicating if the request was successful
- `data.totalOrderAll`: Total count of all orders in the database
- `data.orderSold`: Count of orders with Status = "SOLD"
- `data.orderWaiting`: Count of orders with Status = "WAITING_SELL"
- `data.soldProfitLoss.usdt`: Total profit/loss in USDT (rounded to 2 decimal places)
- `data.soldProfitLoss.thb`: Total profit/loss in Thai Baht (rounded to 2 decimal places)
- `data.soldProfitLoss.exchangeRate`: USD to THB exchange rate used (rounded to 4 decimal places)

### Notes
- Exchange rate is fetched in real-time from exchangerate-api.io
- Rate is cached for 5 minutes to reduce API calls
- If exchange rate API fails, uses cached value or fallback rate (35.00)
- The THB conversion uses the current USD to THB exchange rate at the time of the API call

---

## Common Issues

### Issue: "Username already exists"
- **Solution:** Use a different username or check existing users

### Issue: "Account is locked"
- **Solution:** Account was locked after 5 failed login attempts. Contact administrator to unlock.

### Issue: "Invalid username or password"
- **Solution:** Verify username and password are correct. Check if account is active.

### Issue: Connection Refused
- **Solution:** Make sure the API server is running and check the port number

