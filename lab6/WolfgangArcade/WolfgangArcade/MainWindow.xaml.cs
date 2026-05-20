using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Wolfenstein3DClone
{
    public partial class MainWindow : Window
    {
        // ==================== CONSTS / GRID ====================
        private const int CellSize = 40;
        private const int MapWidth = 19;
        private const int MapHeight = 15;

        // ==================== LEVELS DATA ====================
        private int[][,] levels;
        private int currentLevel = 0;
        private int[,] map;
        private bool isEndlessMode = false;         // флаг бесконечного подземелья
        private Random mapRng = new Random();        // отдельный Random для карт
        private string[] wallTextures = { "wall_brick.png", "wall_stone.png", "wall_blue.png" };

        // ==================== DIFFICULTY ====================
        private double difficultyMultiplier = 1.0;
        private bool isHellMode = false;
        private int enemyCount = 4;
        private int treasureCount = 5;

        // ==================== GAME OBJECTS ====================
        private List<Rectangle> walls = new List<Rectangle>();
        private List<Enemy> enemies = new List<Enemy>();
        private List<Rectangle> treasures = new List<Rectangle>();

        // ==================== PLAYER STATE ====================
        private int playerGridX = 1;
        private int playerGridY = 1;
        private int score = 0;
        private int totalTreasures = 5;
        private int lives = 3;
        private bool isGameRunning = true;
        private bool isInvulnerable = false;
        private bool isMoving = false;
        private DispatcherTimer collisionTimer;
        private double moveSpeed = 0.12;
        private Image PlayerImage;

        // ==================== BFS DIRECTIONS ====================
        private static readonly (int dx, int dy)[] Directions = { (1, 0), (0, 1), (-1, 0), (0, -1) };

        // ==================== PATHS ====================
        private string basePath = @"C:\Users\yorik3k-win\Desktop\lab6\WolfgangArcade\WolfgangArcade\Assets\";

        // ==================== ENEMY CLASS ====================
        private class Enemy
        {
            public Image Image;
            public int GridX, GridY;
            public int PatrolIndex;
            public List<(int x, int y)> PatrolPath;
            public bool IsAlive;
            public bool IsMoving;
            public bool IsChasing;
            public double BaseSpeed;
            public double ChaseSpeed;
        }

        // ==================== INIT ====================
        public MainWindow()
        {
            InitializeComponent();
            InitializeLevels();
            SetupAudio();
            ShowMainMenu();
            BtnNormal.Background = new SolidColorBrush(Color.FromRgb(150, 150, 50));
        }

        // ==================== AUDIO SETUP ====================
        private void SetupAudio()
        {
            MenuMusicPlayer.Source = new Uri(basePath + "menu_music.mp3");
            LevelMusicPlayer.Source = new Uri(basePath + "level_music.mp3");
            HellMusicPlayer.Source = new Uri(basePath + "hell_music.mp3");
            DeathMusicPlayer.Source = new Uri(basePath + "death_music.mp3");
            StepSound.Source = new Uri(basePath + "step.wav");
        }

        private void MenuMusicPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            MenuMusicPlayer.Position = TimeSpan.Zero;
            MenuMusicPlayer.Play();
        }

        private void LevelMusicPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            LevelMusicPlayer.Position = TimeSpan.Zero;
            LevelMusicPlayer.Play();
        }

        private void HellMusicPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            HellMusicPlayer.Position = TimeSpan.Zero;
            HellMusicPlayer.Play();
        }

        private void PlayStepSound()
        {
            StepSound.Stop();
            StepSound.Position = TimeSpan.Zero;
            StepSound.Play();
        }

        // ==================== FAKE LOADING ====================
        private void ShowLoadingThenStart(int levelIndex, bool endless)
        {
            MainMenu.Visibility = Visibility.Hidden;
            DeathMenu.Visibility = Visibility.Hidden;
            GameCanvas.Visibility = Visibility.Hidden;
            HudPanel.Visibility = Visibility.Hidden;
            LoadingScreen.Visibility = Visibility.Visible;

            // анимация точек после текста
            LoadingText.Text = "СТРОИМ СТЕНЫ...";
            DispatcherTimer loadingTimer = new DispatcherTimer();
            loadingTimer.Interval = TimeSpan.FromSeconds(0.5);
            int dotCount = 0;
            loadingTimer.Tick += (s, e) =>
            {
                dotCount = (dotCount + 1) % 4;
                LoadingText.Text = "СТРОИМ СТЕНЫ" + new string('.', dotCount);
            };
            loadingTimer.Start();

            // через 3 секунды — запуск уровня
            DispatcherTimer delayTimer = new DispatcherTimer();
            delayTimer.Interval = TimeSpan.FromSeconds(3);
            delayTimer.Tick += (s, e) =>
            {
                delayTimer.Stop();
                loadingTimer.Stop();
                LoadingScreen.Visibility = Visibility.Hidden;
                GameCanvas.Visibility = Visibility.Visible;
                HudPanel.Visibility = Visibility.Visible;

                if (endless)
                {
                    // генерация случайной карты
                    map = GenerateRandomMap();
                    isEndlessMode = true;
                    currentLevel = 0; // любое, не используется для endless
                    // используем случайную текстуру стен
                }
                else
                {
                    map = levels[levelIndex];
                    isEndlessMode = false;
                    currentLevel = levelIndex;
                }

                StartLevelInternal();
                this.Focus();
            };
            delayTimer.Start();
        }

        // ==================== RANDOM MAP GENERATION ====================
        private int[,] GenerateRandomMap()
        {
            int[,] newMap = new int[MapHeight, MapWidth];

            // 1. Заполняем всё стенами
            for (int y = 0; y < MapHeight; y++)
                for (int x = 0; x < MapWidth; x++)
                    newMap[y, x] = 1;

            // 2. Случайный старт для "крота"
            int startX = mapRng.Next(1, MapWidth - 1);
            int startY = mapRng.Next(1, MapHeight - 1);
            newMap[startY, startX] = 0;

            // 3. "Крот" роет коридоры (Random Walk)
            int currentX = startX;
            int currentY = startY;
            int totalCells = (MapWidth - 2) * (MapHeight - 2);
            int targetOpen = totalCells / 2; // открыть ~50% клеток
            int opened = 1;

            int maxSteps = totalCells * 3;
            int steps = 0;

            while (opened < targetOpen && steps < maxSteps)
            {
                steps++;
                int dir = mapRng.Next(4);
                int nx = currentX + Directions[dir].dx * 2;
                int ny = currentY + Directions[dir].dy * 2;

                if (nx > 0 && nx < MapWidth - 1 && ny > 0 && ny < MapHeight - 1)
                {
                    if (newMap[ny, nx] == 1)
                    {
                        // открываем целевую и промежуточную клетки
                        newMap[ny, nx] = 0;
                        newMap[currentY + Directions[dir].dy, currentX + Directions[dir].dx] = 0;
                        opened += 2;
                    }
                    currentX = nx;
                    currentY = ny;
                }
            }

            // 4. Делаем границу стеной
            for (int y = 0; y < MapHeight; y++)
            {
                newMap[y, 0] = 1;
                newMap[y, MapWidth - 1] = 1;
            }
            for (int x = 0; x < MapWidth; x++)
            {
                newMap[0, x] = 1;
                newMap[MapHeight - 1, x] = 1;
            }

            // 5. Проверяем, что (1,1) — проход (старт игрока)
            if (newMap[1, 1] == 1)
            {
                newMap[1, 1] = 0;
                if (newMap[1, 2] == 1) newMap[1, 2] = 0;
                if (newMap[2, 1] == 1) newMap[2, 1] = 0;
            }

            return newMap;
        }

        // ==================== MAIN MENU ====================
        private void ShowMainMenu()
        {
            isGameRunning = false;
            collisionTimer?.Stop();
            LevelMusicPlayer.Stop();
            HellMusicPlayer.Stop();
            DeathMusicPlayer.Stop();
            StopAllAnimations();

            LoadingScreen.Visibility = Visibility.Hidden;
            GameCanvas.Visibility = Visibility.Hidden;
            HudPanel.Visibility = Visibility.Hidden;
            DeathMenu.Visibility = Visibility.Hidden;
            MainMenu.Visibility = Visibility.Visible;

            MenuMusicPlayer.Play();
        }

        private void StartGameFromMenu(int levelIndex)
        {
            MenuMusicPlayer.Stop();
            DeathMusicPlayer.Stop();

            if (isHellMode)
            {
                LevelMusicPlayer.Stop();
                HellMusicPlayer.Play();
            }
            else
            {
                HellMusicPlayer.Stop();
                LevelMusicPlayer.Play();
            }

            map = levels[levelIndex];
            isEndlessMode = false;
            currentLevel = levelIndex;

            MainMenu.Visibility = Visibility.Hidden;
            DeathMenu.Visibility = Visibility.Hidden;
            GameCanvas.Visibility = Visibility.Visible;
            HudPanel.Visibility = Visibility.Visible;

            StartLevelInternal();
            this.Focus();
        }

        // ==================== MENU BUTTONS ====================
        private void BtnRandom_Click(object sender, RoutedEventArgs e)
        {
            Random rng = new Random();
            StartGameFromMenu(rng.Next(0, 3));
        }
        private void BtnLevel1_Click(object sender, RoutedEventArgs e) => StartGameFromMenu(0);
        private void BtnLevel2_Click(object sender, RoutedEventArgs e) => StartGameFromMenu(1);
        private void BtnLevel3_Click(object sender, RoutedEventArgs e) => StartGameFromMenu(2);
        private void BtnEndless_Click(object sender, RoutedEventArgs e)
        {
            MenuMusicPlayer.Stop();
            DeathMusicPlayer.Stop();

            if (isHellMode)
            {
                LevelMusicPlayer.Stop();
                HellMusicPlayer.Play();
            }
            else
            {
                HellMusicPlayer.Stop();
                LevelMusicPlayer.Play();
            }

            ShowLoadingThenStart(0, true);
        }
        private void BtnExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ==================== DIFFICULTY BUTTONS ====================
        private void BtnEasy_Click(object sender, RoutedEventArgs e)
        {
            difficultyMultiplier = 0.7;
            enemyCount = 3;
            treasureCount = 3;
            isHellMode = false;
            ResetDifficultyButtons();
            BtnEasy.Background = new SolidColorBrush(Color.FromRgb(50, 150, 50));
        }

        private void BtnNormal_Click(object sender, RoutedEventArgs e)
        {
            difficultyMultiplier = 1.0;
            enemyCount = 4;
            treasureCount = 5;
            isHellMode = false;
            ResetDifficultyButtons();
            BtnNormal.Background = new SolidColorBrush(Color.FromRgb(150, 150, 50));
        }

        private void BtnHard_Click(object sender, RoutedEventArgs e)
        {
            difficultyMultiplier = 1.5;
            enemyCount = 4;
            treasureCount = 8;
            isHellMode = false;
            ResetDifficultyButtons();
            BtnHard.Background = new SolidColorBrush(Color.FromRgb(150, 50, 50));
        }

        private void BtnHell_Click(object sender, RoutedEventArgs e)
        {
            difficultyMultiplier = 10.0;
            enemyCount = 5;
            treasureCount = 10;
            isHellMode = true;
            ResetDifficultyButtons();
            BtnHell.Background = new SolidColorBrush(Color.FromRgb(180, 20, 20));
        }

        private void ResetDifficultyButtons()
        {
            BtnEasy.Background = new SolidColorBrush(Color.FromRgb(51, 85, 51));
            BtnNormal.Background = new SolidColorBrush(Color.FromRgb(85, 85, 51));
            BtnHard.Background = new SolidColorBrush(Color.FromRgb(85, 51, 51));
            BtnHell.Background = new SolidColorBrush(Color.FromRgb(40, 10, 10));
        }

        // ==================== DEATH MENU ====================
        private void ShowDeathMenu()
        {
            isGameRunning = false;
            collisionTimer?.Stop();
            LevelMusicPlayer.Stop();
            HellMusicPlayer.Stop();
            MenuMusicPlayer.Stop();
            StopAllAnimations();

            GameCanvas.Visibility = Visibility.Hidden;
            HudPanel.Visibility = Visibility.Hidden;
            MainMenu.Visibility = Visibility.Hidden;
            DeathMenu.Visibility = Visibility.Visible;

            DeathMusicPlayer.Stop();
            DeathMusicPlayer.Position = TimeSpan.Zero;
            DeathMusicPlayer.Play();
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            DeathMusicPlayer.Stop();
            RestartGame();
        }

        private void BtnDeathToMenu_Click(object sender, RoutedEventArgs e)
        {
            DeathMusicPlayer.Stop();
            ShowMainMenu();
        }

        // ==================== MAPS ====================
        private void InitializeLevels()
        {
            levels = new int[3][,];

            // lvl1: Подземелье Замка
            levels[0] = new int[MapHeight, MapWidth]
            {
                {1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1},
                {1,0,0,0,0,0,1,0,0,0,0,0,0,1,0,0,0,0,1},
                {1,0,1,1,1,0,1,0,1,1,1,1,0,1,0,1,1,0,1},
                {1,0,0,0,1,0,0,0,0,0,0,1,0,0,0,0,1,0,1},
                {1,1,1,0,1,1,1,1,1,1,1,1,0,1,1,0,1,0,1},
                {1,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,1},
                {1,0,1,1,1,1,1,1,0,1,0,1,1,0,1,1,1,1,1},
                {1,0,0,0,1,0,0,0,0,1,0,0,0,0,0,0,0,0,1},
                {1,1,1,0,1,0,1,1,1,1,1,1,1,0,1,1,1,0,1},
                {1,0,0,0,0,0,0,0,1,0,0,0,0,0,1,0,0,0,1},
                {1,0,1,1,1,1,0,1,1,0,1,1,1,1,1,0,1,0,1},
                {1,0,0,0,0,1,0,0,0,0,0,0,0,1,0,0,1,0,1},
                {1,1,1,1,0,1,1,1,1,1,1,1,0,1,0,1,1,0,1},
                {1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1},
                {1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1}
            };

            // lvl2: Темница
            levels[1] = new int[MapHeight, MapWidth];
            for (int y = 0; y < MapHeight; y++)
                for (int x = 0; x < MapWidth; x++)
                    levels[1][y, x] = (y == 0 || y == MapHeight - 1 || x == 0 || x == MapWidth - 1) ? 2 : 0;
            for (int y = 2; y < MapHeight - 2; y += 3)
                for (int x = 3; x < MapWidth - 3; x += 4)
                    levels[1][y, x] = 2;
            levels[1][5, 7] = 0; levels[1][5, 11] = 0; levels[1][8, 3] = 0;
            levels[1][8, 15] = 0; levels[1][11, 7] = 0; levels[1][11, 11] = 0;

            // lvl3: Сокровищница
            levels[2] = new int[MapHeight, MapWidth];
            for (int y = 0; y < MapHeight; y++)
                for (int x = 0; x < MapWidth; x++)
                    levels[2][y, x] = 3;
            for (int y = 1; y < MapHeight - 1; y += 2)
                for (int x = 1; x < MapWidth - 1; x++)
                    levels[2][y, x] = 0;
            for (int x = 1; x < MapWidth - 1; x += 2)
                for (int y = 2; y < MapHeight - 1; y += 2)
                    levels[2][y, x] = (x % 4 == 1) ? 0 : 3;
            levels[2][2, 1] = 0; levels[2][2, MapWidth - 2] = 0;
            levels[2][MapHeight - 2, 1] = 0; levels[2][MapHeight - 2, MapWidth - 2] = 0;
            levels[2][1, 1] = 0;
        }

        // ==================== LEVEL LOADER ====================
        private void StartLevelInternal()
        {
            GameCanvas.Children.Clear();
            walls.Clear();
            enemies.Clear();
            treasures.Clear();

            PlayerImage = new Image
            {
                Width = 36,
                Height = 36,
                Source = new BitmapImage(new Uri(basePath + "player_attack.png"))
            };

            BuildWalls();
            SpawnTreasures();
            SpawnEnemies();

            playerGridX = 1;
            playerGridY = 1;
            PlayerImage.Opacity = 1.0;
            Canvas.SetLeft(PlayerImage, playerGridX * CellSize + 2);
            Canvas.SetTop(PlayerImage, playerGridY * CellSize + 2);
            GameCanvas.Children.Add(PlayerImage);
            Panel.SetZIndex(PlayerImage, 100);

            score = 0;
            totalTreasures = treasures.Count;
            lives = 3;
            isGameRunning = true;
            isInvulnerable = false;
            isMoving = false;
            UpdateHUD();

            LevelText.Text = isEndlessMode ? "БЕСКОНЕЧНОЕ" : $"УРОВЕНЬ: {currentLevel + 1}";
            HellLabel.Visibility = isHellMode ? Visibility.Visible : Visibility.Hidden;
            MessageText.Text = "";

            if (collisionTimer == null)
            {
                collisionTimer = new DispatcherTimer();
                collisionTimer.Interval = TimeSpan.FromMilliseconds(50);
                collisionTimer.Tick += CollisionCheck;
            }
            collisionTimer.Start();
        }

        // ==================== WALLS RENDER ====================
        private void BuildWalls()
        {
            // выбор текстуры
            string texture;
            if (isEndlessMode)
            {
                // случайная текстура для endless
                texture = basePath + wallTextures[mapRng.Next(3)];
            }
            else
            {
                texture = basePath + wallTextures[currentLevel];
            }

            for (int y = 0; y < MapHeight; y++)
            {
                for (int x = 0; x < MapWidth; x++)
                {
                    if (map[y, x] > 0)
                    {
                        Rectangle wall = new Rectangle
                        {
                            Width = CellSize,
                            Height = CellSize,
                            Fill = new ImageBrush(new BitmapImage(new Uri(texture))),
                            Stroke = new SolidColorBrush(Color.FromRgb(30, 30, 40)),
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2
                        };
                        Canvas.SetLeft(wall, x * CellSize);
                        Canvas.SetTop(wall, y * CellSize);
                        GameCanvas.Children.Add(wall);
                        walls.Add(wall);
                    }
                }
            }
        }

        // ==================== TREASURES SPAWN ====================
        private void SpawnTreasures()
        {
            List<(int x, int y)> freeCells = new List<(int, int)>();
            for (int y = 1; y < MapHeight - 1; y++)
                for (int x = 1; x < MapWidth - 1; x++)
                    if (map[y, x] == 0 && (x != 1 || y != 1))
                        freeCells.Add((x, y));

            Random rng = new Random();
            var selected = freeCells.OrderBy(_ => rng.Next()).Take(treasureCount).ToList();

            foreach (var pos in selected)
            {
                Rectangle treasure = new Rectangle
                {
                    Width = 20,
                    Height = 20,
                    Fill = new ImageBrush(new BitmapImage(new Uri(basePath + "treasure.png"))),
                    Stroke = new SolidColorBrush(Color.FromRgb(200, 160, 0)),
                    StrokeThickness = 1,
                    RadiusX = 3,
                    RadiusY = 3,
                    Tag = "Treasure"
                };
                Canvas.SetLeft(treasure, pos.x * CellSize + 10);
                Canvas.SetTop(treasure, pos.y * CellSize + 10);
                GameCanvas.Children.Add(treasure);
                Panel.SetZIndex(treasure, 50);
                treasures.Add(treasure);

                DoubleAnimation pulse = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.4,
                    Duration = TimeSpan.FromSeconds(0.5),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                treasure.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            totalTreasures = treasures.Count;
        }

        // ==================== BFS HELPERS ====================
        private bool IsValidCell(int x, int y)
        {
            return x >= 0 && x < MapWidth && y >= 0 && y < MapHeight && map[y, x] == 0;
        }

        private List<(int x, int y)> FindFullPath(int startX, int startY, int endX, int endY)
        {
            if (startX == endX && startY == endY)
                return new List<(int x, int y)> { (startX, startY) };

            var queue = new Queue<(int x, int y)>();
            var visited = new Dictionary<(int x, int y), (int x, int y)>();
            (int x, int y) startNode = (startX, startY);
            queue.Enqueue(startNode);
            visited[startNode] = startNode;

            while (queue.Count > 0)
            {
                (int x, int y) current = queue.Dequeue();
                foreach (var dir in Directions)
                {
                    int nx = current.x + dir.dx;
                    int ny = current.y + dir.dy;
                    if (nx == endX && ny == endY)
                    {
                        var fullPath = new List<(int x, int y)> { (nx, ny) };
                        (int x, int y) node = current;
                        while (node.x != startX || node.y != startY)
                        {
                            fullPath.Add(node);
                            node = visited[node];
                        }
                        fullPath.Reverse();
                        return fullPath;
                    }
                    if (IsValidCell(nx, ny) && !visited.ContainsKey((nx, ny)))
                    {
                        visited[(nx, ny)] = current;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            return null;
        }

        private List<(int x, int y)> GeneratePatrolPath(int startX, int startY)
        {
            Random rng = new Random();
            var allReachable = new List<(int, int)>();
            for (int y = 0; y < MapHeight; y++)
                for (int x = 0; x < MapWidth; x++)
                    if (IsValidCell(x, y))
                        allReachable.Add((x, y));

            var waypoints = new List<(int x, int y)> { (startX, startY) };
            int currentX = startX, currentY = startY;

            for (int i = 0; i < 3; i++)
            {
                (int x, int y) target;
                List<(int x, int y)> segment = null;
                int attempts = 0;
                do
                {
                    target = allReachable[rng.Next(allReachable.Count)];
                    segment = FindFullPath(currentX, currentY, target.x, target.y);
                    attempts++;
                }
                while ((segment == null || segment.Count < 2) && attempts < 50);

                if (segment != null && segment.Count > 0)
                {
                    waypoints.AddRange(segment);
                    currentX = target.x;
                    currentY = target.y;
                }
            }

            var returnPath = FindFullPath(currentX, currentY, startX, startY);
            if (returnPath != null && returnPath.Count > 0)
                waypoints.AddRange(returnPath);

            return waypoints.Count >= 2 ? waypoints : new List<(int, int)> { (startX, startY), (startX + 1, startY) };
        }

        // ==================== ENEMIES SPAWN ====================
        private void SpawnEnemies()
        {
            List<(int x, int y)> spawnCandidates = new List<(int, int)>();
            for (int y = 1; y < MapHeight - 1; y++)
                for (int x = 1; x < MapWidth - 1; x++)
                    if (IsValidCell(x, y) && (Math.Abs(x - 1) > 3 || Math.Abs(y - 1) > 3))
                        spawnCandidates.Add((x, y));

            Random rng = new Random();
            var spawns = spawnCandidates.OrderBy(_ => rng.Next()).Take(enemyCount).ToList();

            foreach (var spawn in spawns)
            {
                Enemy enemy = new Enemy
                {
                    GridX = spawn.x,
                    GridY = spawn.y,
                    PatrolPath = GeneratePatrolPath(spawn.x, spawn.y),
                    PatrolIndex = 0,
                    IsAlive = true,
                    IsMoving = false,
                    IsChasing = false
                };
                CreateEnemySprite(enemy);
            }
        }

        private void CreateEnemySprite(Enemy enemy)
        {
            Image img = new Image
            {
                Width = 36,
                Height = 36,
                Source = new BitmapImage(new Uri(basePath + "enemy_move.png")),
                Tag = "Enemy"
            };
            Canvas.SetLeft(img, enemy.GridX * CellSize + 2);
            Canvas.SetTop(img, enemy.GridY * CellSize + 2);
            GameCanvas.Children.Add(img);
            Panel.SetZIndex(img, 80);
            enemy.Image = img;

            Random rng = new Random();
            enemy.BaseSpeed = (0.3 + rng.NextDouble() * 0.2) * difficultyMultiplier;
            enemy.ChaseSpeed = enemy.BaseSpeed * 1.6;
            enemies.Add(enemy);

            DispatcherTimer startDelay = new DispatcherTimer();
            startDelay.Interval = TimeSpan.FromMilliseconds(rng.Next(200, 800));
            startDelay.Tick += (s, e) =>
            {
                startDelay.Stop();
                if (enemy.IsAlive && isGameRunning)
                    MoveEnemyStep(enemy);
            };
            startDelay.Start();
        }

        // ==================== ENEMY AI ====================
        private void AlertNearbyEnemies(Enemy alertingEnemy)
        {
            foreach (var enemy in enemies)
            {
                if (enemy == alertingEnemy || !enemy.IsAlive || enemy.IsChasing) continue;
                double dist = Math.Sqrt(
                    Math.Pow(enemy.GridX - alertingEnemy.GridX, 2) +
                    Math.Pow(enemy.GridY - alertingEnemy.GridY, 2));
                if (dist < 8) enemy.IsChasing = true;
            }
        }

        private void MoveEnemyStep(Enemy enemy)
        {
            if (!enemy.IsAlive || !isGameRunning || enemy.IsMoving) return;
            if (enemy.PatrolPath == null || enemy.PatrolPath.Count < 2) return;

            double distToPlayer = Math.Sqrt(
                Math.Pow(enemy.GridX - playerGridX, 2) +
                Math.Pow(enemy.GridY - playerGridY, 2));

            if (distToPlayer < 5)
            {
                if (!enemy.IsChasing) AlertNearbyEnemies(enemy);
                enemy.IsChasing = true;
            }
            else if (distToPlayer > 7) enemy.IsChasing = false;

            enemy.IsMoving = true;
            (int x, int y) target;

            if (enemy.IsChasing)
            {
                var path = FindFullPath(enemy.GridX, enemy.GridY, playerGridX, playerGridY);
                if (path != null && path.Count > 0)
                    target = path[0];
                else
                {
                    enemy.IsChasing = false;
                    enemy.PatrolIndex = (enemy.PatrolIndex + 1) % enemy.PatrolPath.Count;
                    target = enemy.PatrolPath[enemy.PatrolIndex];
                }
            }
            else
            {
                enemy.PatrolIndex = (enemy.PatrolIndex + 1) % enemy.PatrolPath.Count;
                target = enemy.PatrolPath[enemy.PatrolIndex];
            }

            double fromLeft = enemy.GridX * CellSize + 2;
            double fromTop = enemy.GridY * CellSize + 2;
            enemy.GridX = target.x;
            enemy.GridY = target.y;
            double toLeft = target.x * CellSize + 2;
            double toTop = target.y * CellSize + 2;

            double distance = Math.Sqrt(Math.Pow(toLeft - fromLeft, 2) + Math.Pow(toTop - fromTop, 2));
            double speed = enemy.IsChasing ? enemy.ChaseSpeed : enemy.BaseSpeed;
            double duration = Math.Max(0.08, (distance / CellSize) / speed);

            DoubleAnimation moveX = new DoubleAnimation
            {
                From = fromLeft,
                To = toLeft,
                Duration = TimeSpan.FromSeconds(duration),
                FillBehavior = FillBehavior.HoldEnd
            };
            DoubleAnimation moveY = new DoubleAnimation
            {
                From = fromTop,
                To = toTop,
                Duration = TimeSpan.FromSeconds(duration),
                FillBehavior = FillBehavior.HoldEnd
            };
            moveX.Completed += (s, e) =>
            {
                enemy.IsMoving = false;
                if (enemy.IsAlive && isGameRunning) MoveEnemyStep(enemy);
            };
            enemy.Image.BeginAnimation(Canvas.LeftProperty, moveX);
            enemy.Image.BeginAnimation(Canvas.TopProperty, moveY);
        }

        // ==================== INPUT ====================
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (DeathMenu.Visibility == Visibility.Visible)
                    {
                        DeathMusicPlayer.Stop();
                        ShowMainMenu();
                        return;
                    }
                    if (GameCanvas.Visibility == Visibility.Visible)
                    {
                        ShowMainMenu();
                        return;
                    }
                    break;
                case Key.R:
                    if (DeathMenu.Visibility == Visibility.Visible)
                    {
                        DeathMusicPlayer.Stop();
                        RestartGame();
                        return;
                    }
                    if (GameCanvas.Visibility == Visibility.Visible)
                    {
                        RestartGame();
                        return;
                    }
                    break;
                case Key.Up: case Key.W: TryMovePlayer(0, -1); break;
                case Key.Down: case Key.S: TryMovePlayer(0, 1); break;
                case Key.Left: case Key.A: TryMovePlayer(-1, 0); break;
                case Key.Right: case Key.D: TryMovePlayer(1, 0); break;
            }
        }

        private void TryMovePlayer(int dx, int dy)
        {
            if (!isGameRunning || isMoving) return;
            int newX = playerGridX + dx;
            int newY = playerGridY + dy;
            if (newX < 0 || newX >= MapWidth || newY < 0 || newY >= MapHeight) return;
            if (map[newY, newX] != 0) return;

            isMoving = true;
            playerGridX = newX;
            playerGridY = newY;
            PlayStepSound();

            DoubleAnimation moveX = new DoubleAnimation
            {
                To = newX * CellSize + 2,
                Duration = TimeSpan.FromSeconds(moveSpeed),
                FillBehavior = FillBehavior.HoldEnd
            };
            DoubleAnimation moveY = new DoubleAnimation
            {
                To = newY * CellSize + 2,
                Duration = TimeSpan.FromSeconds(moveSpeed),
                FillBehavior = FillBehavior.HoldEnd
            };
            moveX.Completed += (s, e) => { isMoving = false; CheckTreasurePickup(); };
            PlayerImage.BeginAnimation(Canvas.LeftProperty, moveX);
            PlayerImage.BeginAnimation(Canvas.TopProperty, moveY);
        }

        private void CheckTreasurePickup()
        {
            if (!isGameRunning) return;
            for (int i = treasures.Count - 1; i >= 0; i--)
            {
                if (!GameCanvas.Children.Contains(treasures[i])) continue;
                int tx = (int)Math.Round((Canvas.GetLeft(treasures[i]) - 10) / CellSize);
                int ty = (int)Math.Round((Canvas.GetTop(treasures[i]) - 10) / CellSize);
                if (tx == playerGridX && ty == playerGridY)
                {
                    var treasure = treasures[i];
                    treasure.BeginAnimation(UIElement.OpacityProperty, null);
                    DoubleAnimation fadeOut = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.0,
                        Duration = TimeSpan.FromSeconds(0.2),
                        FillBehavior = FillBehavior.Stop
                    };
                    fadeOut.Completed += (s, e) =>
                    {
                        if (GameCanvas.Children.Contains(treasure))
                            GameCanvas.Children.Remove(treasure);
                    };
                    treasure.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    treasures.RemoveAt(i);
                    score++;
                    UpdateHUD();
                    if (score >= totalTreasures) { WinLevel(); return; }
                }
            }
        }

        private void UpdateHUD()
        {
            ScoreText.Text = $"СОКРОВИЩ: {score} / {totalTreasures}";
            LivesText.Text = $"ЖИЗНИ: {new string('♥', lives)}";
        }

        private void WinLevel()
        {
            isGameRunning = false;
            collisionTimer?.Stop();
            MessageText.Text = "УРОВЕНЬ ПРОЙДЕН! R - ЗАНОВО, ESC - МЕНЮ";
            MessageText.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));
            DoubleAnimation growW = new DoubleAnimation
            {
                From = 36,
                To = 52,
                Duration = TimeSpan.FromSeconds(0.4),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4)
            };
            DoubleAnimation growH = new DoubleAnimation
            {
                From = 36,
                To = 52,
                Duration = TimeSpan.FromSeconds(0.4),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4)
            };
            PlayerImage.BeginAnimation(WidthProperty, growW);
            PlayerImage.BeginAnimation(HeightProperty, growH);
        }

        private void CollisionCheck(object sender, EventArgs e)
        {
            if (!isGameRunning || isInvulnerable) return;
            double pl = Canvas.GetLeft(PlayerImage);
            double pt = Canvas.GetTop(PlayerImage);
            Rect pb = new Rect(pl, pt, PlayerImage.Width, PlayerImage.Height);
            foreach (var enemy in enemies)
            {
                if (!enemy.IsAlive || !GameCanvas.Children.Contains(enemy.Image)) continue;
                double el = Canvas.GetLeft(enemy.Image);
                double et = Canvas.GetTop(enemy.Image);
                Rect eb = new Rect(el, et, enemy.Image.Width, enemy.Image.Height);
                eb.Inflate(4, 4);
                if (pb.IntersectsWith(eb)) { HitByEnemy(); return; }
            }
        }

        private void HitByEnemy()
        {
            if (isInvulnerable || !isGameRunning) return;
            lives--;
            UpdateHUD();
            if (lives <= 0) { GameOver(); return; }
            isInvulnerable = true;
            isMoving = false;
            PlayerImage.BeginAnimation(Canvas.LeftProperty, null);
            PlayerImage.BeginAnimation(Canvas.TopProperty, null);
            DoubleAnimation blink = new DoubleAnimation
            {
                From = 1.0,
                To = 0.2,
                Duration = TimeSpan.FromSeconds(0.12),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(7)
            };
            blink.Completed += (s, e) => { PlayerImage.Opacity = 1.0; isInvulnerable = false; };
            PlayerImage.BeginAnimation(UIElement.OpacityProperty, blink);
            playerGridX = 1; playerGridY = 1;
            DoubleAnimation kbX = new DoubleAnimation { To = 1 * CellSize + 2, Duration = TimeSpan.FromSeconds(0.4), FillBehavior = FillBehavior.HoldEnd };
            DoubleAnimation kbY = new DoubleAnimation { To = 1 * CellSize + 2, Duration = TimeSpan.FromSeconds(0.4), FillBehavior = FillBehavior.HoldEnd };
            kbX.Completed += (s, e) => { isMoving = false; };
            PlayerImage.BeginAnimation(Canvas.LeftProperty, kbX);
            PlayerImage.BeginAnimation(Canvas.TopProperty, kbY);
            MessageText.Text = $"УРОН! ЖИЗНЕЙ: {lives}";
            MessageText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
        }

        private void GameOver()
        {
            isGameRunning = false;
            collisionTimer?.Stop();
            PlayerImage.BeginAnimation(UIElement.OpacityProperty, null);
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.3,
                Duration = TimeSpan.FromSeconds(0.8),
                FillBehavior = FillBehavior.HoldEnd
            };
            PlayerImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ShowDeathMenu();
        }

        private void RestartGame()
        {
            collisionTimer?.Stop();
            StopAllAnimations();
            enemies.Clear();
            DeathMenu.Visibility = Visibility.Hidden;

            DeathMusicPlayer.Stop();
            if (isHellMode) { LevelMusicPlayer.Stop(); HellMusicPlayer.Play(); }
            else { HellMusicPlayer.Stop(); LevelMusicPlayer.Play(); }

            if (isEndlessMode)
            {
                ShowLoadingThenStart(0, true);
            }
            else
            {
                GameCanvas.Visibility = Visibility.Visible;
                HudPanel.Visibility = Visibility.Visible;
                StartLevelInternal();
                this.Focus();
            }
        }

        private void StopAllAnimations()
        {
            if (PlayerImage != null)
            {
                PlayerImage.BeginAnimation(Canvas.LeftProperty, null);
                PlayerImage.BeginAnimation(Canvas.TopProperty, null);
                PlayerImage.BeginAnimation(UIElement.OpacityProperty, null);
                PlayerImage.BeginAnimation(WidthProperty, null);
                PlayerImage.BeginAnimation(HeightProperty, null);
            }
            foreach (var enemy in enemies)
            {
                if (enemy.Image != null)
                {
                    enemy.Image.BeginAnimation(Canvas.LeftProperty, null);
                    enemy.Image.BeginAnimation(Canvas.TopProperty, null);
                }
                enemy.IsAlive = false;
                enemy.IsMoving = false;
            }
        }
    }
}