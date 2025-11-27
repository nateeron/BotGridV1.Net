using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BotGridV1.Models
{
    public class HashCode_pass
    {
        public string password { get; set; }
    }

    public class HashCode_resp
    {
        public string password { get; set; }
        public string hashCode { get; set; }
        public bool status { get; set; }
    }

    public class User
    {
        internal static ClaimsIdentity Identity;

        public string UserName { get; set; }
        public string UserRole { get; set; }
    }

    public class reqLogin
    {
        public string username { get; set; }
        public string password { get; set; }
    }

  

    public class req_Registration
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Nationality { get; set; }
        public IFormFile ImageFile { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string Preferren { get; set; }
        public string BankAccount { get; set; }
        public string BankName { get; set; }
        public string SWIFTCode { get; set; }
        public string ExpertiseID { get; set; }

    }
    public class req_RegistrationTEST
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public IFormFile ImageFile { get; set; }


    }

    public class req_InsertUserLogin_
    {
        public string LoginName { get; set; }
       
    }
    public class req_InsertUserLogin
    {
        public int? loginID { get; set; }
        public string LoginName { get; set; }
        public string LoginPassword { get; set; }
        public string LoginEmail { get; set; }
        public bool IsActive { get; set; }
        public bool IsDelete { get; set; }
        public string ApproveBy { get; set; }
        public bool? isNew { get; set; }
        public List<int> RoleIDs { get; set; }
        public List<int> CompanyIDs { get; set; }
        public string Action { get; set; }

        
    }

    public class req_SignUp
    {
        public string username { get; set; }
        public string password { get; set; }
        public string email { get; set; }
        public string ApproveBy { get; set; }
        public string CreateBy { get; set; }
        public List<int> RoleIDs { get; set; }
        public List<int> CompanyIDs { get; set; }
    }



    public class resp_SignUp
    {
        public int status { get; set; }
       

    }


    public class respLogin
    {
        public int status { get; set; }
        public string userToken { get; set; }
    }


    public class Token_code {
        public string token { get; set; }

    }

    public class req_Decode
    {
        public string Key { get; set; }
        public string EncryptedText { get; set; }

    }

    public class req_GetUserList_ByRoleLevel
    {
        public int RoleID { get; set; }
    }

    public class resp_GetUserList_ByRoleLevel
    {
        public int LoginID { get; set; }
        public string LoginName { get; set; }
        public string LoginEmail { get; set; }
        public int RoleID { get; set; }
        public string RoleName { get; set; }
        public int RoleLevel { get; set; }
        public bool IsActive { get; set; }
        public bool? isNew { get; set; }
        public string ApproveBy { get; set; }
    }
    public class resp_GetUserList_ByRoleLevel2
    {
        public int LoginID { get; set; }
        public string LoginName { get; set; }
        public string LoginEmail { get; set; }
        public int RoleID { get; set; }
        public string RoleName { get; set; }
        public int RoleLevel { get; set; }
        public bool IsActive { get; set; }
        public bool? isNew { get; set; }
        public string ApproveBy { get; set; }
        public List<int> RoleIDs { get; set; }
        public List<int> CompanyIDs { get; set; }
        public List<RoleDetail> roleAll { get; set; }
        public List<CompanyPerCountry> companyPer { get; set; }
        public int SelectCom { get; set; }
    }

    public class RoleDetail
    {
        public int roleIDs { get; set; }
        public string roleName { get; set; }
    }

    public class CompanyPerCountry
    {
        public int CountryID { get; set; }
        public string CountryName { get; set; }
        public List<CompanyDetail> Companys { get; set; }
    }

    public class CompanyDetail
    {
        public int companyID { get; set; }
        public string companyName { get; set; }
    }

    // Comparer classes for Union operations
    public class RoleDetailComparer : IEqualityComparer<RoleDetail>
    {
        public bool Equals(RoleDetail x, RoleDetail y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.roleIDs == y.roleIDs;
        }

        public int GetHashCode(RoleDetail obj)
        {
            return obj?.roleIDs.GetHashCode() ?? 0;
        }
    }

    public class CompanyPerCountryComparer : IEqualityComparer<CompanyPerCountry>
    {
        public bool Equals(CompanyPerCountry x, CompanyPerCountry y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.CountryID == y.CountryID;
        }

        public int GetHashCode(CompanyPerCountry obj)
        {
            return obj?.CountryID.GetHashCode() ?? 0;
        }
    }

    // MAP_User_Company Models
    public class req_UserInfo
    {
        public int loginID { get; set; }
    }

    public class req_GetUserCompanyMapping
    {
        public int? UserID { get; set; }
    }

    public class resp_UserCompanyMapping
    {
        public int MAP_CompanyID { get; set; }
        public int UserID { get; set; }
        public int CompanyID { get; set; }
        public int CountryFC_ID { get; set; }
        public string LoginName { get; set; }
        public string LoginEmail { get; set; }
        public bool? isNew { get; set; }
        public string LoginUserID { get; set; }
        public string CountryFC_NameEN { get; set; }
        public string CountryFC_NameTH { get; set; }
        public string CountryFC_Code { get; set; }
        public string CurrencyFC { get; set; }
        public string CompanyCode { get; set; }
        public string CompanyName { get; set; }
        public int IsActive { get; set; }
        public int IsDelete { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime? UpdateDate { get; set; }
        public string CreateBy { get; set; }
        public string UpdateBy { get; set; }
    }

    public class req_InsertUserCompanyMapping
    {
        public int UserID { get; set; }
        public int CompanyID { get; set; }
        public int CountryFC_ID { get; set; }
        public int IsActive { get; set; } = 1;
        public int IsDelete { get; set; } = 0;
        public string CreateBy { get; set; }
        public string UpdateBy { get; set; }
    }

    public class req_UpdateUserCompanyMapping
    {
        public int MAP_CompanyID { get; set; }
        public int UserID { get; set; }
        public int CompanyID { get; set; }
        public int CountryFC_ID { get; set; }
        public int IsActive { get; set; }
        public int IsDelete { get; set; }
        public string UpdateBy { get; set; }
    }

    public class req_DeleteUserCompanyMapping
    {
        public int MAP_CompanyID { get; set; }
        public string UpdateBy { get; set; }
    }

    public class resp_UserCompanyMappingResult
    {
        public int MAP_CompanyID { get; set; }
        public int RowsAffected { get; set; }
        public string Message { get; set; }
    }

    // Organization Info Models
    public class req_OrganizationInfo
    {
        public int? CompanyID { get; set; }
        public int? CountryFC_ID { get; set; }
        public string CompanyCode { get; set; }
        public string CompanyName { get; set; }
        public string TaxID { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public int? IsActive { get; set; }
        public int? IsDelete { get; set; }
    }

    public class resp_OrganizationInfo
    {
        public int CompanyID { get; set; }
        public int? CountryFC_ID { get; set; }
        public string CompanyCode { get; set; }
        public int NumberEM { get; set; }
        public string CompanyName { get; set; }
        public string CompanyNameEN { get; set; }
        public string TaxID { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public int? IsActive { get; set; }
        public int? IsDelete { get; set; }
        public string CountryFC_Code { get; set; }
        public string CountryFC_NameEN { get; set; }
        public string CountryFC_NameTH { get; set; }
        public string CurrencyFC { get; set; }
        public string TimeZone { get; set; }
    }

    // Country Master Models
    public class req_GetOrgani
    {
        public int? CompanyID { get; set; }
    }

    public class req_GetCountryMaster
    {
        public int? CountryFC_ID { get; set; }
    }

    public class resp_CountryMaster
    {
        public int CountryFC_ID { get; set; }
        public string CountryFC_Code { get; set; }
        public string CountryFC_NameEN { get; set; }
        public string CountryFC_NameTH { get; set; }
        public string CurrencyFC { get; set; }
        public string TimeZone { get; set; }
        public bool IsActive { get; set; }
    }

    // LoginSetUp Models
    public class req_LoginSetUp
    {
        public int? LoginID { get; set; }
        public int? SelectCom { get; set; }
        public int? modeTheme { get; set; }
    }

    public class resp_LoginSetUp
    {
        public int LoginID { get; set; }
        public int SelectCom { get; set; }
        public int modeTheme { get; set; }
    }

    // Registration Stored Procedure Response Models
    public class resp_InsertUserRegistration
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int? UserProfileID { get; set; }
        public string UserLoginID { get; set; }
        public UserProfileData UserProfile { get; set; }
        public RegistrationData Registration { get; set; }
        public List<RecruiterExpertiseData> RecruiterExpertise { get; set; }
    }

    public class UserProfileData
    {
        public int UserProfileID { get; set; }
        public string UserLoginID { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Nationality { get; set; }
        public string PathImage { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string Preferren { get; set; }
        public string BankAccount { get; set; }
        public string BankName { get; set; }
        public string SWIFTCode { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public string CreateBy { get; set; }
        public string UpdateBy { get; set; }
    }

    public class RegistrationData
    {
        public string UserID { get; set; }
        public string UserPassword { get; set; }
        public string UserLogin { get; set; }
        public bool IsActive { get; set; }
        public bool IsDelete { get; set; }
        public DateTime CreateDate { get; set; }
        public string CreateBy { get; set; }
        public DateTime UpdateDate { get; set; }
        public string UpdateBy { get; set; }
        public string ResetToken { get; set; }
    }

    public class req_ResetPassword
    {
        public string NewPassword { get; set; }
    }

    public class RecruiterExpertiseData
    {
        public string UserID { get; set; }
        public int ExpertiseID { get; set; }
        public string OtherExpertise { get; set; }
    }

    public class req_UpdateProfile
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        public IFormFile? ImageFile { get; set; }  // Optional - not required
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Preferren { get; set; }
        public string? BankAccount { get; set; }
        public string? BankName { get; set; }
        public string? SWIFTCode { get; set; }
        public string? ExpertiseID { get; set; }
    }

    public class resp_GetProfile
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public UserProfileData? UserProfile { get; set; }
        public List<RecruiterExpertiseData>? RecruiterExpertise { get; set; }
    }

    public class resp_UpdateProfile
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class req_ChangePassword
    {
        public string Username { get; set; }
        public string Password { get; set; }  // Old password
        public string NewPassword { get; set; }
    }

    public class resp_ChangePassword
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}
