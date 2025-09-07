# AITSYS.Discord.LibraryDevelopmentTracking

This bot is a .NET-based Discord bot designed to track library development progress and interact with Notion for discord alpha tests.

## Note

This project only works for notions based on the following template: https://www.notion.so/marketplace/templates/discord-lib-devs-implementations-tracking

If you want to use it with other notions, you'll need to modify the code yourself. But be warned, notions API is a shithole :^)

## Features

- **Discord Bot Integration**: Connects to Discord and responds to commands and interactions.
- **Notion Integration**: Fetches and updates project data from Notion databases.
- **Application Commands**: Supports Discord slash commands for user interaction.
- **Configurable**: Uses a JSON configuration file for easy setup.

## Project Structure `src`

<details>

<summary>Structure</summary>	
 
- `Program.cs`: Entry point for the application.
- `DiscordBot.cs`: Main bot logic and Discord client setup.
- `ApplicationCommands.cs`: Implementation of Discord application (slash) commands.
- `Interactions.cs`: Handles Discord interaction events.
- `Providers.cs`: Abstraction for data providers (e.g., Notion).
- `NotionRestClient.cs`: REST client for Notion API.
- `Entities/`: Contains data models and Notion-related entities.
- `config.json`: Configuration file (see `config.example.json` for template).

</details>


## Getting Started

<details>

<summary>Getting Started</summary>

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- A Discord bot token
- Notion integration token and corresponding IDs

### Setup
1. **Clone the repository:**
   ```sh
   git clone https://github.com/Aiko-IT-Systems/AITSYS.Discord.LibraryDevelopmentTracking.git
   cd AITSYS.Discord.LibraryDevelopmentTracking/src
   ```
2. **Configure the bot:**
   - Copy `config.example.json` to `config.json` and fill in your credentials and settings.

3. **Build the project:**
   ```sh
   dotnet build
   ```

4. **Run the bot:**
   ```sh
   dotnet run
   ```

### Publishing
For Linux deployment, use the provided `publish.sh` script:
```sh
cd AITSYS.Discord.LibraryDevelopmentTracking/linux
./publish.sh
```

</details>

## Contributing
At the current stage, contributions are not being accepted. Future plans may include opening the project for community contributions.

## License
This project is licensed under the <b>AGPL-3.0-or-later</b> License. See `LICENSE.md` and `LICENSE_NOTICE.md` for details.

## Acknowledgements
- [DisCatSharp](https://github.com/Aiko-IT-Systems/DisCatSharp) for Discord API integration
- [Notion API](https://developers.notion.com/) for project management features
