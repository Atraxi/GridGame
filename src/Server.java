import spark.Spark;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;

public class Server
{
    private static final Map<Integer, Game> games = Collections.synchronizedMap(new HashMap<Integer, Game>());

    public static void main(String[] args) throws IOException {
        String indexHTML = new String(Files.readAllBytes(Paths.get("index.html")));
        String createHTML = new String(Files.readAllBytes(Paths.get("create.html")));
        String webSocketChatHTML = new String(Files.readAllBytes(Paths.get("webSocketChat.html")));

        Spark.port(80);

        Spark.externalStaticFileLocation("public");

        Spark.webSocket("/game", SocketGameNetworkLayer.class);

        Spark.get("/create/submit", (request, response) ->
        {
            String name = request.queryParams("name");
            if(!name.matches("[a-zA-Z0-9_\\-&(),.+ ]*"))
            {
                //TODO:response.status();
                return "Input rejected";
            }
            int size;
            try {
                size = Integer.valueOf(request.queryParams("size"));
                if(size < 5)
                {
                    throw new NumberFormatException("Value too small, rejected");
                }
            }
            catch (NumberFormatException e)
            {
                //TODO:response.status();
                return "Input rejected";
            }
            response.redirect("/game/" + createGame(name, size).hashCode());
            return "success";
        });

        Spark.get("/create", (request, response) ->
        {
            return createHTML;
        });

        Spark.get("/game/:gameID/:player/:x/:y/:xOffset/:yOffset", (request, response) ->
        {
            Game game = games.get(Integer.parseInt(request.params(":gameID")));
            game.update(Integer.parseInt(request.params(":x")), Integer.parseInt(request.params(":y")), request.params(":player"), Integer.parseInt(request.params(":xOffset")), Integer.parseInt(request.params(":yOffset")));
            return game.draw(request.params(":player"));
        });

        Spark.get("/game/:gameID/:player/:x/:y", (request, response) ->
        {
            Game game = games.get(Integer.parseInt(request.params(":gameID")));
            game.update(Integer.parseInt(request.params(":x")), Integer.parseInt(request.params(":y")), request.params(":player"), 1, 1);
            return game.draw(request.params(":player"));
        });

        Spark.get("/game/:gameID/:player", (request,response) ->
        {
            return games.get(Integer.parseInt(request.params(":gameID"))).draw(request.params(":player"));
        });

        Spark.get("/game/:gameID", (request, response) ->
        {
            return "temporarily empty, TODO: add 'lobby'";
        });

        Spark.get("/game", (request, response) ->
        {
            return webSocketChatHTML;
        });

        //default route, home page
        Spark.get("/", (request, response) ->
        {
            StringBuilder gameList = new StringBuilder();

            ArrayList<Game> gamesList = new ArrayList<>(games.values());
            Collections.sort(gamesList);
            for(Game game : gamesList)
            {
                gameList.append("			")
                        .append(game.getName())
                        .append("\t<a href=\"/game/")
                        .append(game.hashCode())
                        .append("\">Join</a>")
                        .append("</br>\n");
            }
            return indexHTML.replace("<!--INSERT_GAME_LIST-->", gameList.toString());
        });
    }

    public static Game getGame(int gameHash)
    {
        return games.get(gameHash);
    }

    public static Game createGame(String name, int size) throws IOException
    {
        Game newGame = new Game(name, size);
        games.put(newGame.hashCode(), newGame);
        return newGame;
    }

    public static void EndGame(int finishedGameHash)
    {
        games.remove(finishedGameHash);
    }
}