using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using MySql.Data.MySqlClient;
using System.Net;
using System.Data;

// JSON 직렬화 설정
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Function
{
    public class Function
    {
        private readonly string dbHost;
        private readonly string dbUser;
        private readonly string dbPass;
        private readonly string dbName;

        public Function()
        {
            // 1. 환경 변수에서 DB 접속 정보 로드
            dbHost = Environment.GetEnvironmentVariable("DB_HOST");
            dbUser = Environment.GetEnvironmentVariable("DB_USER");
            dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
            dbName = Environment.GetEnvironmentVariable("DB_NAME");
        }

        /// <summary>
        /// /auth/login POST 요청을 처리
        /// </summary>
        public async Task<APIGatewayProxyResponse> Login(APIGatewayProxyRequest request, ILambdaContext context)
        {
            // Unity가 보낸 JSON Body를 파싱
            RequestLogin loginRequest = new RequestLogin();
            try
            {
                loginRequest = JsonSerializer.Deserialize<RequestLogin>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("JSON 파싱 오류: {0}"), e.Message);
                return CreateResponse(HttpStatusCode.BadRequest, "요청 형식이 잘못되었습니다.");
            }

            try
            {
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbName, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                // 3. ID를 기반으로 DB에 저장된 해시된 비밀번호를 먼저 가져옴
                string sqlGetHash = "SELECT PW FROM `User` WHERE ID = @ID";
                await using var cmdGetHash = new MySqlCommand(sqlGetHash, conn);
                cmdGetHash.Parameters.AddWithValue("@ID", loginRequest.id);

                string storedHash = null;
                await using (var readerHash = await cmdGetHash.ExecuteReaderAsync())
                {
                    if (await readerHash.ReadAsync())
                    {
                        storedHash = readerHash.GetString("PW");
                    }
                }

                // 4. ID가 존재하지 않거나 BCrypt로 비밀번호를 비교했는데 일치하지 않는 경우
                if (storedHash == null || !BCrypt.Net.BCrypt.Verify(loginRequest.pw, storedHash))
                {
                    // 5. (실패) 일치하는 유저 없음
                    context.Logger.LogWarning(string.Format("로그인 실패: {0}", loginRequest.id));
                    return CreateResponse(HttpStatusCode.Unauthorized, "ID 또는 비밀번호가 일치하지 않습니다.");
                }

                // 6. (성공) 비밀번호가 일치하면 나머지 유저 정보를 가져옴
                string sqlGetUserData = "SELECT UID, Name, Level, EXP, ClearedStageCode FROM `User` WHERE ID = @ID";
                await using var cmdGetUserData = new MySqlCommand(sqlGetUserData, conn);
                cmdGetUserData.Parameters.AddWithValue("@ID", loginRequest.id);

                await using var reader = await cmdGetUserData.ExecuteReaderAsync();

                // 7. 유저 존재 여부 확인
                if (await reader.ReadAsync())
                {
                    // 8. (성공) 로그인 성공. 유저 데이터를 응답으로 보냄
                    UserInfo user = new UserInfo();
                    user.uid = reader.GetInt32("UID");
                    user.name = reader.GetString("Name");
                    user.level = reader.GetInt32("Level");
                    user.exp = reader.GetInt32("EXP");
                    user.clearStageCode = reader.IsDBNull(reader.GetOrdinal("ClearedStageCode")) ? null : reader.GetString("ClearedStageCode");
                    return CreateResponse(HttpStatusCode.OK, user);
                }
                else
                {
                    // 8. (실패) 일치하는 유저 없음
                    context.Logger.LogWarning(string.Format("로그인 실패: {0}", loginRequest.id));
                    return CreateResponse(HttpStatusCode.Unauthorized, "ID 또는 비밀번호가 일치하지 않습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        // API Gateway 응답 생성 헬퍼
        private APIGatewayProxyResponse CreateResponse(HttpStatusCode statusCode, object result)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonSerializer.Serialize(result),
                Headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Access-Control-Allow-Origin", "*" }
            }
            };
        }
    }
}