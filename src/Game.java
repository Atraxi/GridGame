import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Paths;

public class Game implements Comparable<Game>
{
	private final String name;
	private final int[][] board;
	private static final int GRIDSIZE = 16;
	private int currentPlayerTurn = 1;
	private int remainingMoves = 3;
	private static String gameBoard;
	
	public Game(String name, int size) throws IOException {
		this.name = name;
		//buffer the map edges to prevent indexing issues (very memory inefficent, but easier to implement)
		board = new int[size + 4][size + 4];
		board[2][2] = 1;
		board[board.length - 1 - 2][board[0].length - 1 - 2] = 2;
        gameBoard = new String(Files.readAllBytes(Paths.get("gameBoard.html"))).replace("GRIDSIZE", String.valueOf(GRIDSIZE));
	}
	
	public String draw(String player)
	{
		StringBuilder html = new StringBuilder();
		for(int x = 2; x < board.length - 2; x++)
		{
			html.append(
		"			<tr>\n"
			);
			for(int y = 2; y < board[x].length - 2; y++)
			{
				html.append(
		"				<td class=\"tile" + board[x][y] + "\" width=10 height=10 onclick=\"buildLink(" + (x - 2) + ", " + (y - 2) + ")\" onmouseover=\"\" style=\"cursor: pointer;\">\n" +
		"				</td>\n"
				);
			}
			html.append(
		"		</tr>\n"
			);
		}
		return gameBoard.replace("HASHCODE", String.valueOf(this.hashCode())).replace("PLAYER", player).replace("<!--INSERT_GAME_BOARD-->", html.toString()).replace("<!--INSERT_TURN_INFO-->",
		"    <pre id=\"turnInfo\">Current player:" + (currentPlayerTurn==1?"Green":"Red") + "\nTurns remaining:" + remainingMoves + "</pre>"
		);
	}
	
	public void update(int x, int y, String player, int xOriginOffset, int yOriginOffset)
	{
		//TODO sanitize
		//convert player string to integer representation
		int playerIndex = 0;
		if(player.equals("green"))
		{
			playerIndex = 1;
		}
		else if(player.equals("red"))
		{
			playerIndex = 2;
		}
		else return;

        //check for a known to be irrelevant click
        if(board[x][y] == -1 || board[x][y] == playerIndex)
        {
            return;
        }
		
		//check which players turn it is, and if they are at the end of their turn
		if(remainingMoves < 1)
		{
			remainingMoves = 3;
			currentPlayerTurn = currentPlayerTurn==1?2:1;
		}
		if(currentPlayerTurn != playerIndex)
		{
			return;
		}
		
		//basic move, i.e. create new token
		for(int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				if(board[x+i][y+j] == playerIndex)
				{
					if(board[x][y] == 0)
					{
						board[x][y] = playerIndex;
                        remainingMoves--;
                        if(remainingMoves < 1)
                        {
                            remainingMoves = 3;
                            currentPlayerTurn = currentPlayerTurn==1?2:1;
                        }
                        return;
					}
					else if(board[x][y] != playerIndex && board[x][y] != -1)
					{
						board[x][y] = -1;
                        remainingMoves--;
                        if(remainingMoves < 1)
                        {
                            remainingMoves = 3;
                            currentPlayerTurn = currentPlayerTurn==1?2:1;
                        }
                        return;
					}
				}
			}
		}
		
		//jump move, check if there is more than one possible origin location
		int legalMoveCount = 0;
		for(int i = -2; i <= 2; i+=2)
		{
			for (int j = -2; j <= 2; j+=2)
			{
				if(board[x+i][y+j] == playerIndex)
				{
					legalMoveCount++;
				}
			}
		}
		
		//There is only one possible origin location for a jump
		if(legalMoveCount == 1)
		{
			if(board[x][y] == 0)
			{
				board[x][y] = playerIndex;
			}
			else if(board[x][y] != playerIndex)
			{
				board[x][y] = -1;
			}
			
			//Kill origin location
			for(int i = -2; i <= 2; i+=2)
			{
				for (int j = -2; j <= 2; j+=2)
				{
					if(board[x+i][y+j] == playerIndex && !(i==0 && j==0))
					{
						board[x+i][y+j] = -1;
						remainingMoves--;
                        if(remainingMoves < 1)
                        {
                            remainingMoves = 3;
                            currentPlayerTurn = currentPlayerTurn==1?2:1;
                        }
						return;
					}
				}
			}
		}
		//There are multiple possible origin location, 
		else if(legalMoveCount > 1)
		{
			//verify inputs are within acceptable bounds
			boolean xCheck = xOriginOffset == 0 || Math.abs(xOriginOffset) == 2;
			boolean yCheck = yOriginOffset == 0 || Math.abs(yOriginOffset) == 2;
			boolean combinedCheck = xCheck && yCheck && !(xOriginOffset == 0 && yOriginOffset == 0);
			
			//verify inputs refer to a legal move
			if(board[x+xOriginOffset][y+yOriginOffset] == playerIndex && combinedCheck)
			{
				if(board[x][y] == 0)
				{
					board[x][y] = playerIndex;
                    board[x+xOriginOffset][y+yOriginOffset] = -1;
                    remainingMoves--;
                    if(remainingMoves < 1)
                    {
                        remainingMoves = 3;
                        currentPlayerTurn = currentPlayerTurn==1?2:1;
                    }
				}
				else if(board[x][y] != playerIndex)
				{
					board[x][y] = -1;
                    board[x+xOriginOffset][y+yOriginOffset] = -1;
                    remainingMoves--;
                    if(remainingMoves < 1)
                    {
                        remainingMoves = 3;
                        currentPlayerTurn = currentPlayerTurn==1?2:1;
                    }
				}
			}
		}
	}
	
	public String getName()
	{
		return name;
	}

	@Override
	public int compareTo(Game otherGame) {
		return this.getName().compareTo(otherGame.getName());
	}
}