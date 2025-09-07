# AITSYS.Discord.LibraryDevelopmentTracking

AITSYS.Discord.LibraryDevelopmentTracking is a .NET-based Discord bot designed to track library development progress and interact with Notion for project management. It leverages the DisCatSharp library for Discord integration and supports extensible command and interaction handling.

## Features

- **Discord Bot Integration**: Connects to Discord and responds to commands and interactions.
- **Notion Integration**: Fetches and updates project data from Notion databases.
- **Application Commands**: Supports Discord slash commands for user interaction.
- **Configurable**: Uses a JSON configuration file for easy setup.
- **Extensible**: Modular codebase for adding new features and providers.

## Project Structure

- `Program.cs`: Entry point for the application.
- `DiscordBot.cs`: Main bot logic and Discord client setup.
- `ApplicationCommands.cs`: Implementation of Discord application (slash) commands.
- `Interactions.cs`: Handles Discord interaction events.
- `Providers.cs`: Abstraction for data providers (e.g., Notion).
- `NotionRestClient.cs`: REST client for Notion API.
- `Entities/`: Contains data models and Notion-related entities.
- `config.json`: Configuration file (see `config.example.json` for template).

## Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- A Discord bot token
- Notion integration token and corresponding IDs (if using Notion features)

### Setup
1. **Clone the repository:**
   ```sh
   git clone https://github.com/Aiko-IT-Systems/AITSYS.Discord.LibraryDevelopmentTracking.git
   cd AITSYS.Discord.LibraryDevelopmentTracking
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
For Linux deployment, use the provided `publish_linux.sh` script:
```sh
./publish_linux.sh
```

## Contributing
At the current stage, contributions are not being accepted. Future plans may include opening the project for community contributions.

## License
This project is licensed under the AGPL-3.0-or-later License. See `LICENSE.md` and `LICENSE_NOTICE.md` for details.

## Acknowledgements
- [DisCatSharp](https://github.com/Aiko-IT-Systems/DisCatSharp) for Discord API integration
- [Notion API](https://developers.notion.com/) for project management features
