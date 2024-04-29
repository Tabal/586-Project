var express = require('express'),
    async = require('async'),
    sql = require('mssql'), //mssql replaces Pool which was used for pgsql.
    cookieParser = require('cookie-parser'),
    app = express(),
    server = require('http').Server(app),
    io = require('socket.io')(server);

var port = process.env.PORT || 4000;

io.on('connection', function (socket) {

  socket.emit('message', { text : 'Welcome!' });

  socket.on('subscribe', function (data) {
    socket.join(data.channel);
  });
});

//Create the config for the SQL Server connection
var config = "Data Source=db,1433;Initial Catalog=master;User ID=sa;Password=YourStrong@Password;Encrypt=false"

async.retry(
  {times: 1000, interval: 1000},
  function(callback) {
    //Also pass in the config
    sql.connect(config, (err, client) => {
      if (err) {
        console.error("Waiting for db");
      }
      callback(err, client);
    });
  },
  function(err, client) {
    if (err) {
      return console.error("Giving up");
    }
    console.log("Connected to db");
    getVotes(client);
  }
);

async function getVotes(client) {
  console.log('Querying to get votes');
  try {
    const result = await sql.query('SELECT vote, COUNT(*) AS count FROM votes GROUP BY vote')
    var votes = collectVotesFromResult(result);
    io.sockets.emit("scores", JSON.stringify(votes));
  } catch (err) {
    console.error("Error performing query: " + err);
  }

  //Retry for awhile before timing out.
  setTimeout(async () => {
    await getVotes(client) 
  }, 1000);
}

function collectVotesFromResult(result) {
  var votes = {a: 0, b: 0, c: 0};

  result.recordset.forEach(function (row) {
    votes[row.vote] = parseInt(row.count);
  });
  console.log(votes);
  return votes;
}

app.use(cookieParser());
app.use(express.urlencoded());
app.use(express.static(__dirname + '/views'));

app.get('/', function (req, res) {
  res.sendFile(path.resolve(__dirname + '/views/index.html'));
});

//Handle errors during connections
sql.on('error', err => {
  console.error('SQL Server error:',err);
});

server.listen(port, function () {
  var port = server.address().port;
  console.log('App running on port ' + port);
});
