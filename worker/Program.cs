using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient; //This is used 
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Worker
{
    [ApiController]
    [Route("")]
    public class Program : ControllerBase

    {

        public static SqlConnection sqlConn = OpenDbConnection("Data Source=db,1433;Initial Catalog=master;User ID=sa;Password=YourStrong@Password;Encrypt=false");
        public static int Main(string[] args)
        {
            try
            {
                var redisConn = OpenRedisConnection("redis");
                var redis = redisConn.GetDatabase();

                var keepAliveCommand = sqlConn.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                var definition = new { vote = "", voter_id = "" };
                while (true)
                {
                    // Slow down to prevent CPU spike, only query each 100ms
                    Thread.Sleep(100);

                    // Reconnect redis if down
                    if (redisConn == null || !redisConn.IsConnected) {
                        Console.WriteLine("Reconnecting Redis");
                        redisConn = OpenRedisConnection("redis");
                        redis = redisConn.GetDatabase();
                    }
                    string json = redis.ListLeftPopAsync("votes").Result;
                    if (json != null)
                    {
                        var vote = JsonConvert.DeserializeAnonymousType(json, definition);
                        Console.WriteLine($"Processing vote for '{vote.vote}' by '{vote.voter_id}'");
                        // Reconnect DB if down
                        if (!sqlConn.State.Equals(System.Data.ConnectionState.Open))
                        {
                            Console.WriteLine("Reconnecting DB");
                            sqlConn = OpenDbConnection("Data Source=db,1433;Initial Catalog=master;User Id=sa;Password=YourStrong@Password;Encrypt=false");
                        }
                    }
                    else
                    {
                        keepAliveCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static SqlConnection OpenDbConnection(string connectionString)
        {
            SqlConnection connection;

            while (true)
            {
                try
                {
                    connection = new SqlConnection(connectionString);
                    connection.Open();
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Waiting for db --Socket Ex");
                    Thread.Sleep(1000);
                }
                catch (DbException ex)
                {
                    Console.Error.WriteLine("Waiting for DB --DB Ex" + ex);
                    Thread.Sleep(1000);
                }
            }

            Console.Error.WriteLine("Connected to db");

            var command = new SqlCommand("createVotesTable", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.ExecuteNonQuery();

            return connection;
        }

        private static ConnectionMultiplexer OpenRedisConnection(string hostname)
        {
            // Use IP address to workaround https://github.com/StackExchange/StackExchange.Redis/issues/410
            var ipAddress = GetIp(hostname);
            Console.WriteLine($"Found redis at {ipAddress}");

            while (true)
            {
                try
                {
                    Console.Error.WriteLine("Connecting to redis");
                    return ConnectionMultiplexer.Connect(ipAddress);
                }
                catch (RedisConnectionException)
                {
                    Console.Error.WriteLine("Waiting for redis");
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetIp(string hostname)
            => Dns.GetHostEntryAsync(hostname)
                .Result
                .AddressList
                .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToString();


        [HttpPost("updateVote")]
        public IActionResult UpdateVote([FromBody] Voter voter)
        {
            var command = sqlConn.CreateCommand();
            try
            {
                command.CommandText = "INSERT INTO votes (id, vote) VALUES (@id, @vote)";
                command.Parameters.AddWithValue("@id", voter.voterId);
                command.Parameters.AddWithValue("@vote", voter.vote);
                command.ExecuteNonQuery();

                return Ok();
            }
            catch (DbException)
            {
                command.CommandText = "UPDATE votes SET vote = @vote WHERE id = @id";
                command.ExecuteNonQuery();

                return Ok();
            }
            finally
            {
                command.Dispose();
            }
       
        } 
    }

    public class Voter
    {
        public string voterId;
        public string vote;
    }
}