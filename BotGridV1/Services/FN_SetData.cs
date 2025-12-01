


using BotGridV1.Models;
using JwtAppLogin.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BotGridV1.Services
{
    public class FN_SetData
    {
        private string logFilePath;
        private string _constrings;
        private string _Key;

        private readonly HttpClient _httpClient;

        public FN_SetData(IConfiguration config)
        {

            _httpClient = new HttpClient();
            _constrings = config.GetConnectionString("Connection_Decode");
            logFilePath = config.GetConnectionString("errorlog");
            _Key = "FMS_API"; 
        }


     
        public async Task<string> Decode(string str)
        {
            req_Decode oj = new req_Decode();
            oj.Key= _Key;
            oj.EncryptedText= str;

            var json = JsonConvert.SerializeObject(oj);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Use full URL instead of setting BaseAddress to avoid "already started" error
            string fullUrl = _constrings.TrimEnd('/') + "/SHA/SHAs/Decrypt";
            HttpResponseMessage response = await _httpClient.PostAsync(fullUrl, content);

            if (response.IsSuccessStatusCode)
            {
                // ✅ สำเร็จ
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                // ❌ ถ้า fail
                string error = await response.Content.ReadAsStringAsync();
                return $"Error {response.StatusCode}: {error}";
            }
        }

      

        public object CheckNull(SqlDataReader reader, string columnName, Type type)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (!reader.IsDBNull(ordinal))
                {
                    Type targetType = Nullable.GetUnderlyingType(type) ?? type;
                    return Convert.ChangeType(reader[columnName], targetType);
                    //return Convert.ChangeType(reader[columnName], type);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception)
            {

                return null;

            }
        }

      

        //public void SetDatas(ClientConnection model, SqlDataReader reader, string propertyName)
        //{
        //    PropertyInfo property = typeof(ClientConnection).GetProperty(propertyName);
        //    if (property != null)
        //    {
        //        object value = CheckNull(reader, propertyName, property.PropertyType);
        //        property.SetValue(model, value);
        //    }
        //}

        //public void SetDatas(ClientConnection model, SqlDataReader reader, string propertyName)
        //{
        //    PropertyInfo property = typeof(ClientConnection).GetProperty(propertyName);
        //    if (property != null)
        //    {
        //        object value = CheckNull(reader, propertyName, property.PropertyType);
        //        property.SetValue(model, value);
        //    }
        //}



        /// <summary>
        /// Generate random key length 10 + attach username
        /// </summary>
        public string GenerateUserToken()
        {
            // สร้าง Random string 10 ตัวอักษร (A-Z, a-z, 0-9)
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new RNGCryptoServiceProvider();
            var tokenChars = new char[10];

            byte[] data = new byte[10];
            random.GetBytes(data);

            for (int i = 0; i < tokenChars.Length; i++)
            {
                var idx = data[i] % chars.Length;
                tokenChars[i] = chars[idx];
            }

            string randomKey = new string(tokenChars);

            // ผูกกับ username (อาจจะเข้ารหัสเพิ่มได้)
            string userToken = randomKey;

            return userToken;
        }

        public void WriteLog(string message)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(logFilePath, true))
                {
                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                    writer.WriteLine(logEntry);
                }

            }
            catch (Exception)
            {

            }
        }
    }
}
