import org.eclipse.jetty.websocket.api.Session;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketClose;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketConnect;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketMessage;
import org.eclipse.jetty.websocket.api.annotations.WebSocket;

import java.io.IOException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Created by Atraxi on 17/01/2016.
 */

@WebSocket
public class SocketGameNetworkLayer {
    //A game can appear multiple times via different sessions
    private static HashMap<Session, GameDetail> games = new HashMap<Session, GameDetail>();

    @OnWebSocketConnect
    public void connect(Session session)
    {
        System.out.println("Connection attempt received");

        Map<String, List<String>> paramMap = session.getUpgradeRequest().getParameterMap();

        int gamehash;
        try
        {
            gamehash = Integer.parseInt(paramMap.get("gamehash").get(0));
        }
        catch (Exception e)
        {
            e.printStackTrace();
            try
            {
                session.getRemote().sendString("Error accessing or parsing gamehash");
            }
            catch (IOException e1)
            {
                e1.printStackTrace();
            }
            return;
        }
        String player;
        try
        {
            player = paramMap.get("player").get(0);
        }
        catch (Exception e)
        {
            e.printStackTrace();
            try
            {
                session.getRemote().sendString("Error accessing or parsing player");
            }
            catch (IOException e1)
            {
                e1.printStackTrace();
            }
            return;
        }

        Game game = Server.getGame(gamehash);
        if(game != null && player != null)
        {
            games.put(session, new GameDetail(player,Server.getGame(gamehash)));
        }
        else
        {
            try
            {
                session.getRemote().sendString("Could not find existing game with that hash");
            }
            catch (IOException e1)
            {
                e1.printStackTrace();
            }
            return;
        }
    }

    @OnWebSocketClose
    public void closed(Session session, int statusCode, String reason)
    {
        System.out.println("Session closed, code:" + statusCode + " reason:" + reason);
        Server.EndGame(games.get(session).hashCode());
        games.remove(session);
    }

    @OnWebSocketMessage
    public void message(Session session, String message)
    {
        GameDetail game = games.get(session);
        game.game.update(message, game.player);
    }

    private class GameDetail
    {
        private String player;
        private Game game;

        private GameDetail(String player, Game game)
        {
            this.player = player;
            this.game = game;
        }
    }
}
