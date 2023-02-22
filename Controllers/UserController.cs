using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;


namespace Auth_REST_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IDocumentClient _documentClient;
        // private readonly ILogger<UserController> _logger;
        private readonly String _databaseId;
        private readonly String _collectionId;
        public IConfiguration Configuration { get; }

        public UserController(IDocumentClient documentClient, IConfiguration configuration)
        {
            _documentClient = documentClient;
            Configuration = configuration;
            _databaseId = Configuration["CosmosDB:DatabaseId"];
            _collectionId = Configuration["CosmosDB:UserCollectionId"];

            BuildCollection().Wait();
        }

        private async Task BuildCollection()
        {
            await _documentClient.CreateDatabaseIfNotExistsAsync(new Database { Id = _databaseId });
            await _documentClient.CreateDocumentCollectionIfNotExistsAsync(
                UriFactory.CreateDatabaseUri(_databaseId),
                new DocumentCollection { Id = _collectionId }
            );
        }

        [AllowAnonymous]
        [HttpPost("signup")]
        public async Task<ActionResult> SignUp([FromBody] Models.User newUser)
        {


            var existingUser = _documentClient.CreateDocumentQuery<Models.User>(
                UriFactory.
                CreateDocumentCollectionUri(_databaseId, _collectionId))
                .Where((u) => u.Email == newUser.Email);

            if (existingUser != null)
            {
                return Conflict("User already exists");
            }

            // Hash the password and save the user to the database
            newUser.Password = BCrypt.Net.BCrypt.HashPassword(newUser.Password);

            await _documentClient.CreateDocumentAsync(
                UriFactory.CreateDocumentCollectionUri(_databaseId, _collectionId),
                newUser
            );

            return Ok("User created successfully");
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] Models.User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Jwt:Key"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var existingUser = _documentClient.CreateDocumentQuery<Models.User>(
                UriFactory.CreateDocumentCollectionUri(_databaseId, _collectionId))
                .Where(u => u.Email == user.Email)
                .FirstOrDefault();

            if (existingUser == null)
            {
                return NotFound("User not found");
            }

            if (!BCrypt.Net.BCrypt.Verify(user.Password, existingUser.Password))
            {
                return Forbid("Invalid credentials");
            }


            var token = new JwtSecurityToken(
                Configuration["Jwt:Issuer"],
                Configuration["Jwt:Audience"],
                expires: DateTime.Now.AddMinutes(15),
                signingCredentials: credentials
              );

            return Ok(new { token });
        }


        [HttpGet]
        public async Task<ActionResult> Get(int perPageCount = 20)
        {
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = perPageCount };

            var users = _documentClient.CreateDocumentQuery<Models.User>(
                UriFactory.CreateDocumentCollectionUri(_databaseId, _collectionId),
                queryOptions
               );


            return Ok(new
            {
                count = users.Count(),
                data = users
            });
        }


        [HttpGet("{id}")]
        public async Task<ActionResult> Get(string id)
        {
            var user = _documentClient.CreateDocumentQuery<Models.User>(
                UriFactory.CreateDocumentCollectionUri(_databaseId, _collectionId))
                .Where(u => u.Id == id)
                .FirstOrDefault();

            if (user == null)
            {
                return NotFound("User not found!");
            }

            return Ok(new { data = user });
        }


    }
}
