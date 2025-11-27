namespace JwtAppLogin.Models
{
	public class Model_SetRole
	{
	}
	public class SetRoles
	{
		public string users { set; get; }
		public int level { set; get; }
		public string UserID { set; get; }
		public int expiresMin { set; get; }
    }

	public class req
	{
		public string username { set; get; }
		public string password { set; get; }
	}
}
