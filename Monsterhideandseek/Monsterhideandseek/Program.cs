using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
//Я буквально через ИИ-ку писал комментарии чтобы вам било проще. Да и проект еще сирой... мда...

// ============================================================================
// КОНСОЛЬНЫЙ ПЛАТФОРМЕР С ИИ МОНСТРОМ
// ============================================================================
// Игрок управляет персонажем (A/D - движение, S - присесть, Пробел - прыжок)
// Нужно прятаться от монстра за стенами, приседая (S), чтобы он потерял игрока
// Монстр патрулирует карту, преследует при виде, осматривает активированные игрушки
// ============================================================================

class SmoothPlatformer
{
    // === НАСТРОЙКИ ЭКРАНА ===================================================
    static int nScreenWidth = 120;    // Ширина консоли в символах
    static int nScreenHeight = 40;    // Высота консоли в символах

    // === БУФЕР ЭКРАНА (ДВОЙНАЯ БУФЕРИЗАЦИЯ) ==================================
    // Используем WinAPI для прямого вывода в консоль без мерцания
    static char[] screen;             // Массив символов всего экрана (ширина × высота)
    static IntPtr hConsole;           // Дескриптор консольного буфера (Windows API)
    static uint dwBytesWritten = 0;   // Служебная переменная для WriteConsoleOutputCharacter
    static Random rand = new Random(); // Генератор случайных чисел для объектов

    // === ПАРАМЕТРЫ ИГРОКА ===================================================
    static float fPlayerX = 30.0f;    // Текущая X координата (float для плавности)
    static float fPlayerY = 10.0f;    // Текущая Y координата
    static float fVelocityX = 0.0f;   // Скорость по горизонтали (движение)
    static float fVelocityY = 0.0f;   // Скорость по вертикали (прыжок/падение)

    // === ФИЗИЧЕСКИЕ КОНСТАНТЫ ================================================
    static float fMoveSpeed = 40.0f;      // Скорость бега влево/вправо (символов в секунду)
    static float fCrouchSpeed = 15.0f;    // Скорость при приседании (медленнее)
    static float fGravity = 80.0f;        // Ускорение свободного падения (условные единицы)
    static float fJumpForce = 35.0f;      // Начальная скорость прыжка (отрицательная по Y)
    static int nGroundLevel;              // Y-координата пола (вычисляется в Main)

    // === СОСТОЯНИЯ ИГРОКА ===================================================
    static bool bCrouching = false;       // true = игрок присел (ниже, медленнее, нельзя прыгать)
    static bool bOnGround = false;        // true = стоит на земле (можно прыгать)

    // === ПАРАМЕТРЫ МОНСТРА ==================================================
    static float fMonsterX = 100.0f;      // Начальная позиция X (справа на экране)
    static float fMonsterY = 0.0f;        // Y координата (устанавливается на уровень пола)
    static float fMonsterSpeed = 25.0f;   // Скорость патрулирования
    static float fMonsterChaseSpeed = 45.0f; // Скорость погони (быстрее игрока!)
    static bool bMonsterDirRight = false; // Направление патруля: false = влево, true = вправо
    static float fMonsterTimer = 0.0f;    // Таймер для состояния Search (ищет 2 секунды)
    static float fInvestigateX = 0.0f;    // Точка, куда монстр идет при поиске игрушки

    // === СПРАЙТЫ ============================================================
    // Массив строк = спрайт. Каждая строка - линия символов. Пробел = прозрачность.

    // Спрайт монстра (можно менять на любые символы)
    static string[] monsterSprite = new string[]
    {
        "◊◊◊",  // Голова
        " ◊ ",  // Туловище
        "◊ ◊"   // Ноги
    };

    // Состояния ИИ монстра
    enum MonsterState
    {
        Patrol,      // Патрулирование влево-вправо
        Chase,       // Активная погоня за игроком
        Search,      // Потерял игрока из виду, ищет 2 секунды
        Investigate  // Идет проверять место, где наступили на игрушку
    }
    static MonsterState monsterState = MonsterState.Patrol; // Текущее состояние ИИ
    static bool bGameOver = false;      // true = игра закончена (монстр поймал игрока)

    // === ОБЪЕКТЫ УРОВНЯ =====================================================
    // Стена: прямоугольник из символов '░', блокирует линию видимости, монстр проходит сквозь
    struct Wall
    {
        public int X, Y;        // Левый верхний угол
        public int Width, Height; // Размеры (обычно 3-5 в ширину, 2 в высоту)
    }

    // Игрушка: точка на полу (символ '⊞'), при активации (наступании) привлекает монстра
    struct Toy
    {
        public int X, Y;        // Координаты
        public bool Active;     // false = уже наступили, монстр идет сюда
    }

    static List<Wall> walls = new List<Wall>();   // Список всех стен на уровне
    static List<Toy> toys = new List<Toy>();      // Список всех игрушек

    // === СПРАЙТЫ ИГРОКА =====================================================
    // Обычная стойка: высота 4 строки, видна из-за стены (голова торчит)
    static string[] playerSpriteStand = new string[]
    {
        " █ ",  // Голова
        "███",  // Туловище
        " █ ",  // Пояс
        "███"   // Ноги
    };

    // Присед: высота 2 строки, можно спрятаться за стену полностью
    static string[] playerSpriteCrouch = new string[]
    {
        "███",  // Туловище (голова внутри)
        "███"   // Ноги (согнуты)
    };

    static string[] currentSprite; // Текущий активный спрайт (зависит от приседания)

    // === СИМВОЛЫ ОТРИСОВКИ ==================================================
    const char FLOOR_TILE = '▓';  // Символ пола (можно заменить на '#' если не отображается)
    const char WALL_TILE = '░';   // Символ стены (блокирует видимость)
    const char TOY_TILE = '⊞';    // Символ игрушки (триггер для монстра)

    // === WIN32 API ДЛЯ РАБОТЫ С КОНСОЛЬЮ =====================================
    // Структура координат для Windows API
    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
    }

    // Импорт функций из kernel32.dll для прямого управления консолью (без мерцания)
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateConsoleScreenBuffer(uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwFlags, IntPtr lpScreenBufferData);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleActiveScreenBuffer(IntPtr hConsoleOutput);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool WriteConsoleOutputCharacter(IntPtr hConsoleOutput, char[] lpCharacter, uint nLength, COORD dwWriteCoord, out uint lpNumberOfCharsWritten);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey); // Для опроса клавиш (A, D, S, Space, Esc)

    [DllImport("kernel32.dll")]
    static extern void Sleep(uint dwMilliseconds);  // Задержка между кадрами

    // Константы Windows API
    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint CONSOLE_TEXTMODE_BUFFER = 1;

    // ============================================================================
    // ГЛАВНЫЙ МЕТОД (ТОЧКА ВХОДА)
    // ============================================================================
    static void Main()
    {
        // Настройка размера окна консоли
        Console.SetWindowSize(nScreenWidth, nScreenHeight);
        Console.SetBufferSize(nScreenWidth, nScreenHeight);
        Console.CursorVisible = false;  // Скрыть мигающий курсор
        Console.Title = "Platformer - Crouch to hide!";

        // Создание отдельного буфера экрана (двойная буферизация - никакого мерцания!)
        screen = new char[nScreenWidth * nScreenHeight];
        hConsole = CreateConsoleScreenBuffer(GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, CONSOLE_TEXTMODE_BUFFER, IntPtr.Zero);
        SetConsoleActiveScreenBuffer(hConsole);  // Переключиться на наш буфер

        // Установка уровня пола (за 3 строки до низа экрана)
        nGroundLevel = nScreenHeight - 3;

        // Игрок начинает стоя
        currentSprite = playerSpriteStand;

        // Установить монстра и игрока на высоту пола (с учетом высоты спрайтов)
        fMonsterY = nGroundLevel - monsterSprite.Length;
        fPlayerY = nGroundLevel - playerSpriteStand.Length;

        // Генерация случайных стен и игрушек (1-5 каждого)
        GenerateObjects();

        // Переменные для расчета времени между кадрами (дельта-тайм)
        DateTime tp1 = DateTime.UtcNow;
        DateTime tp2 = DateTime.UtcNow;

        // ============================================================================
        // ГЛАВНЫЙ ИГРОВОЙ ЦИКЛ
        // ============================================================================
        while (!bGameOver)
        {
            // 1. Расчет времени с предыдущего кадра (для плавной анимации)
            tp2 = DateTime.UtcNow;
            float fElapsedTime = (float)(tp2 - tp1).TotalSeconds;
            tp1 = tp2;

            // Защита от слишком больших задержек (фризов)
            if (fElapsedTime <= 0) fElapsedTime = 0.001f;
            if (fElapsedTime > 0.1f) fElapsedTime = 0.1f;

            // 2. Обработка ввода с клавиатуры
            ProcessInput();

            // 3. Физика игрока (гравитация, движение, ограничения)
            UpdatePhysics(fElapsedTime);

            // 4. Логика ИИ монстра (состояния, принятие решений)
            UpdateMonster(fElapsedTime);

            // 5. Проверка наступания на игрушки
            CheckToyPickup();

            // 6. Проверка смерти игрока (только если монстр видит!)
            CheckGameOver();

            // 7. Отрисовка всего на экран (пол, стены, игрушки, монстр, игрок, UI)
            Render();

            // Небольшая задержка чтобы не грузить CPU на 100%
            Sleep(5);
        }

        // Если вышли из цикла - показать экран смерти
        ShowGameOver();
    }

    // ============================================================================
    // ГЕНЕРАЦИЯ УРОВНЯ (СТЕНЫ И ИГРУШКИ)
    // ============================================================================
    // Создает 1-5 стен (высота 2, ширина 3-5) и 1-5 игрушек (1x1)
    // Объекты не спавнятся в зоне игрока (безопасный радиус) и не перекрывают друг друга
    static void GenerateObjects()
    {
        // Опасная зона вокруг стартовой позиции игрока (чтобы не застрять)
        int safeZoneLeft = (int)fPlayerX - 20;
        int safeZoneRight = (int)fPlayerX + 20;

        // --- ГЕНЕРАЦИЯ СТЕН (от 1 до 5) ---
        int wallCount = rand.Next(1, 6);  // Next(1,6) = от 1 до 5 включительно
        for (int i = 0; i < wallCount; i++)
        {
            int attempts = 0;
            // Пытаемся разместить стену до 50 раз (если не влазит - пропускаем)
            while (attempts < 50)
            {
                int w = rand.Next(3, 6);  // Ширина: 3, 4 или 5
                int x = rand.Next(5, nScreenWidth - w - 5);  // Случайная X (с отступом от краев)
                int y = nGroundLevel - 2;  // Y: стоит на полу, высота 2 (как присевший игрок)

                // Проверка: не попадает ли в безопасную зону спавна
                if (x + w > safeZoneLeft && x < safeZoneRight) { attempts++; continue; }

                // Проверка: не перекрывается ли с уже существующими стенами
                bool overlaps = false;
                foreach (var existing in walls)
                    if (x < existing.X + existing.Width && x + w > existing.X) { overlaps = true; break; }

                // Если все ок - добавляем стену в список
                if (!overlaps) { walls.Add(new Wall { X = x, Y = y, Width = w, Height = 2 }); break; }
                attempts++;
            }
        }

        // --- ГЕНЕРАЦИЯ ИГРУШЕК (от 1 до 5) ---
        int toyCount = rand.Next(1, 6);
        for (int i = 0; i < toyCount; i++)
        {
            int attempts = 0;
            while (attempts < 50)
            {
                int x = rand.Next(5, nScreenWidth - 5);
                int y = nGroundLevel - 1;  // На полу, высота 1

                // Не в зоне спавна игрока
                if (x > safeZoneLeft && x < safeZoneRight) { attempts++; continue; }

                // Не внутри стены
                bool insideWall = false;
                foreach (var wall in walls)
                    if (x >= wall.X && x < wall.X + wall.Width && y >= wall.Y && y < wall.Y + wall.Height) { insideWall = true; break; }

                // Не на другой игрушке
                bool onOther = false;
                foreach (var toy in toys)
                    if (toy.X == x && toy.Y == y) { onOther = true; break; }

                if (!insideWall && !onOther) { toys.Add(new Toy { X = x, Y = y, Active = true }); break; }
                attempts++;
            }
        }
    }

    // ============================================================================
    // ОБРАБОТКА ВВОДА С КЛАВИАТУРЫ
    // ============================================================================
    // A / D - движение (скорость зависит от приседания)
    // S - присесть (меняет спрайт, скорость, позицию Y, можно прятаться)
    // Space - прыжок (только стоя на земле)
    // Esc - выход
    static void ProcessInput()
    {
        float currentSpeed = fMoveSpeed;

        // --- ПРЫЖОК ---
        // Работает только если: кнопка Space, стоим на земле, не присели
        if ((GetAsyncKeyState((int)ConsoleKey.Spacebar) & 0x8000) != 0 && bOnGround && !bCrouching)
            fVelocityY = -fJumpForce;  // Отрицательная скорость = движение вверх против гравитации

        // --- ПРИСЕДАНИЕ (механика скрытности) ---
        if ((GetAsyncKeyState('S') & 0x8000) != 0)
        {
            if (!bCrouching)
            {
                // Переход в режим приседания:
                // 1. Меняем флаг
                bCrouching = true;
                // 2. Корректируем Y позицию, чтобы ноги остались на месте (а не парили в воздухе)
                // Разница в высоте между стоячим и присевшим спрайтом
                fPlayerY += playerSpriteStand.Length - playerSpriteCrouch.Length;
            }
            currentSprite = playerSpriteCrouch;  // Меняем спрайт на низкий
            currentSpeed = fCrouchSpeed;          // Замедляемся
        }
        else
        {
            // --- ВСТАТЬ ---
            if (bCrouching)
            {
                bCrouching = false;
                // Корректируем Y обратно (вверх)
                fPlayerY -= playerSpriteStand.Length - playerSpriteCrouch.Length;
            }
            currentSprite = playerSpriteStand;  // Обычный спрайт
        }

        // --- ГОРИЗОНТАЛЬНОЕ ДВИЖЕНИЕ ---
        fVelocityX = 0;  // По умолчанию стоим на месте

        // GetAsyncKeyState возвращает бит 15 (0x8000) если кнопка сейчас нажата
        if ((GetAsyncKeyState('A') & 0x8000) != 0) fVelocityX = -currentSpeed;  // Влево (минус X)
        if ((GetAsyncKeyState('D') & 0x8000) != 0) fVelocityX = currentSpeed;   // Вправо (плюс X)

        // Выход из игры
        if ((GetAsyncKeyState((int)ConsoleKey.Escape) & 0x8000) != 0) bGameOver = true;
    }

    // ============================================================================
    // ФИЗИКА ИГРОКА
    // ============================================================================
    // Применяет скорость к позиции, обрабатывает гравитацию и столкновение с полом
    // dt - дельта-тайм (время с прошлого кадра) для плавности на разных FPS
    static void UpdatePhysics(float dt)
    {
        // Движение по горизонтали (X += скорость * время)
        fPlayerX += fVelocityX * dt;

        // Гравитация: увеличиваем скорость падения (Y положительный = вниз)
        fVelocityY += fGravity * dt;
        fPlayerY += fVelocityY * dt;

        // --- ПРОВЕРКА ПОЛА ---
        // Нижняя точка спрайта игрока
        int nSpriteHeight = currentSprite.Length;
        float fFootY = fPlayerY + nSpriteHeight;

        // Если ноги ушли ниже уровня пола - ставим на пол и сбрасываем скорость падения
        if (fFootY >= nGroundLevel)
        {
            fPlayerY = nGroundLevel - nSpriteHeight;
            fVelocityY = 0;        // Не падаем
            bOnGround = true;      // Можно прыгать
        }
        else
        {
            bOnGround = false;     // В воздухе
        }

        // --- ГРАНИЦЫ ЭКРАНА ПО X ---
        // Не даем выйти за левый край (0) и правый край (ширина - размер спрайта)
        int nSpriteWidth = GetMaxSpriteWidth(currentSprite);
        if (fPlayerX < 0) fPlayerX = 0;
        if (fPlayerX > nScreenWidth - nSpriteWidth) fPlayerX = nScreenWidth - nSpriteWidth;
    }

    // ============================================================================
    // ИСКУССТВЕННЫЙ ИНТЕЛЛЕКТ МОНСТРА (КОНЕЧНЫЙ АВТОМАТ СОСТОЯНИЙ)
    // ============================================================================
    // Состояния:
    // Patrol - идет влево-вправо по экрану, разворачивается у краев
    // Chase - видит игрока, бежит к нему с повышенной скоростью
    // Search - потерял игрока из виду (тот спрятался присев за стеной), стоит 2 сек
    // Investigate - игрок наступил на игрушку, монстр идет проверять место преступления
    static void UpdateMonster(float dt)
    {
        // Проверяем, видит ли монстр игрока сейчас (учитывает стены и приседание)
        bool canSee = CanSeePlayer();

        // --- МАШИНА СОСТОЯНИЙ ---
        switch (monsterState)
        {
            case MonsterState.Patrol:
                // Движение: влево или вправо в зависимости от флага направления
                if (bMonsterDirRight)
                {
                    fMonsterX += fMonsterSpeed * dt;
                    // Достиг правого края - разворот
                    if (fMonsterX >= nScreenWidth - GetMaxSpriteWidth(monsterSprite))
                    {
                        fMonsterX = nScreenWidth - GetMaxSpriteWidth(monsterSprite);
                        bMonsterDirRight = false;
                    }
                }
                else
                {
                    fMonsterX -= fMonsterSpeed * dt;
                    // Достиг левого края - разворот
                    if (fMonsterX <= 0)
                    {
                        fMonsterX = 0;
                        bMonsterDirRight = true;
                    }
                }

                // Увидел игрока - переключаемся на погоню
                if (canSee)
                    monsterState = MonsterState.Chase;
                break;

            case MonsterState.Chase:
                if (!canSee)
                {
                    // Игрок спрятался! Переходим в поиск (стоим на месте 2 сек)
                    monsterState = MonsterState.Search;
                    fMonsterTimer = 2.0f;
                }
                else
                {
                    // Преследуем: движемся в сторону игрока по X
                    if (fPlayerX > fMonsterX) fMonsterX += fMonsterChaseSpeed * dt;
                    else fMonsterX -= fMonsterChaseSpeed * dt;
                }
                break;

            case MonsterState.Search:
                // Отсчитываем время поиска
                fMonsterTimer -= dt;
                if (fMonsterTimer <= 0)
                {
                    // Время вышло - возвращаемся к патрулю
                    monsterState = MonsterState.Patrol;
                    // Определяем направление: к какому краю ближе, туда и идем
                    bMonsterDirRight = (fMonsterX < nScreenWidth / 2);
                }
                // Если снова увидел игрока во время поиска - снова преследуем
                if (canSee)
                    monsterState = MonsterState.Chase;
                break;

            case MonsterState.Investigate:
                // Движемся к месту активации игрушки (fInvestigateX)
                if (Math.Abs(fMonsterX - fInvestigateX) < 1.0f)
                {
                    // Дошли - возвращаемся к патрулю
                    monsterState = MonsterState.Patrol;
                }
                else
                {
                    // Идем к точке
                    if (fInvestigateX > fMonsterX) fMonsterX += fMonsterSpeed * dt;
                    else fMonsterX -= fMonsterSpeed * dt;
                }

                // Если по дороге увидел игрока - бросаем игрушку, гонимся за ним
                if (canSee)
                    monsterState = MonsterState.Chase;
                break;
        }
        // Примечание: монстра не проверяем на столкновение со стенами - он проходит сквозь них (фантом)
    }

    // ============================================================================
    // ПРОВЕРКА ВИДИМОСТИ (ЛОГИКА СКРЫТНОСТИ)
    // ============================================================================
    // Монстр видит игрока если:
    // 1. Расстояние < 40 символов
    // 2. Между ними нет стены ИЛИ игрок стоит (если стена есть и игрок присел - не видит!)
    static bool CanSeePlayer()
    {
        // Проверка дистанции видимости (40 символов по горизонтали)
        if (Math.Abs(fPlayerX - fMonsterX) > 40) return false;

        // Проверяем, есть ли стена между монстром и игроком
        if (IsWallBetween())
        {
            // Есть стена!
            // Если игрок присел (bCrouching) - он спрятался за стеной, монстр не видит
            if (bCrouching) return false;

            // Если стоит - голова торчит из-за стены, монстр видит!
            return true;
        }

        // Нет стены - видит всегда (в пределах дистанции)
        return true;
    }

    // Вспомогательный метод: проверяет, пересекает ли линия между монстром и игроком какую-либо стену
    static bool IsWallBetween()
    {
        // Диапазон X между монстром и игроком
        float startX = Math.Min(fMonsterX + 1, fPlayerX + 1);
        float endX = Math.Max(fMonsterX + 1, fPlayerX + 1);

        // Уровень "глаз" монстра (примерно середина его высоты)
        int y = (int)(fMonsterY + monsterSprite.Length / 2);

        // Проверяем все стены
        foreach (var wall in walls)
        {
            // Пересечение по X: стена находится между монстром и игроком
            if (wall.X <= endX && wall.X + wall.Width >= startX)
            {
                // Пересечение по Y: стена достаточно высокая, чтобы закрыть линию взгляда
                // Стены имеют высоту 2, игрок приседом тоже 2 - идеально прячется
                if (y >= wall.Y && y <= wall.Y + wall.Height)
                    return true; // Есть препятствие на пути взгляда
            }
        }
        return false; // Стен нет, видимость свободна
    }

    // ============================================================================
    // ПРОВЕРКА НАСТУПАНИЯ НА ИГРУШКИ (ТРИГГЕРЫ)
    // ============================================================================
    // Если игрок касается активной игрушки - она деактивируется и монстр идет сюда
    static void CheckToyPickup()
    {
        // Размеры коллизии игрока
        int pWidth = GetMaxSpriteWidth(currentSprite);
        int pHeight = currentSprite.Length;

        // Проверяем все игрушки
        for (int i = 0; i < toys.Count; i++)
        {
            var toy = toys[i];
            if (!toy.Active) continue;  // Уже наступали - пропускаем

            // Проверка пересечения прямоугольников (AABB collision)
            // Игрок слева от правого края игрушки И игрок справа от левого края
            // И игрок выше нижнего края И игрок ниже верхнего края игрушки
            if (fPlayerX < toy.X + 1 && fPlayerX + pWidth > toy.X &&
                fPlayerY < toy.Y + 1 && fPlayerY + pHeight > toy.Y)
            {
                // НАСТУПИЛИ НА ИГРУШКУ!
                toys[i] = new Toy { X = toy.X, Y = toy.Y, Active = false }; // Деактивируем

                // Монстр переключается на расследование
                fInvestigateX = toy.X;  // Запоминаем координату X куда идти
                monsterState = MonsterState.Investigate;
                break; // Обрабатываем одну игрушку за кадр
            }
        }
    }

    // ============================================================================
    // ПРОВЕРКА СМЕРТИ ИГРОКА
    // ============================================================================
    // Монстр убивает только если:
    // 1. Произошло столкновение спрайтов (пересечение прямоугольников)
    // 2. Монстр ВИДИТ игрока в этот момент (если спрятался за стеной - не убьет!)
    static void CheckGameOver()
    {
        // КРИТИЧЕСКАЯ ПРОВЕРКА: если монстр не видит игрока (тот спрятался) - убийства нет!
        if (!CanSeePlayer()) return;

        // Размеры спрайтов для проверки столкновений
        int pWidth = GetMaxSpriteWidth(currentSprite);
        int pHeight = currentSprite.Length;
        int mWidth = GetMaxSpriteWidth(monsterSprite);
        int mHeight = monsterSprite.Length;

        // Проверка пересечения двух прямоугольников (AABB)
        // Если игрок слева от правого края монстра И справа от левого края
        // И выше нижнего края И ниже верхнего края - значит пересекаются
        if (fPlayerX < fMonsterX + mWidth &&
            fPlayerX + pWidth > fMonsterX &&
            fPlayerY < fMonsterY + mHeight &&
            fPlayerY + pHeight > fMonsterY)
        {
            bGameOver = true;  // Игра окончена
        }
    }

    // ============================================================================
    // РЕНДЕРИНГ (ОТРИСОВКА КАДРА)
    // ============================================================================
    // 1. Очищает буфер
    // 2. Рисует пол
    // 3. Рисует стены
    // 4. Рисует активные игрушки
    // 5. Рисует монстра
    // 6. Рисует игрока (поверх всего, чтобы был "спереди")
    // 7. Рисует интерфейс (UI)
    // 8. Выводит на экран одним вызовом API (мгновенно, без мерцания)
    static void Render()
    {
        // 1. Очистка экрана (заполняем пробелами)
        Array.Fill(screen, ' ');

        // 2. Рисуем пол (две линии внизу для толщины)
        for (int x = 0; x < nScreenWidth; x++)
        {
            screen[nGroundLevel * nScreenWidth + x] = FLOOR_TILE;
            if (nGroundLevel + 1 < nScreenHeight)
                screen[(nGroundLevel + 1) * nScreenWidth + x] = FLOOR_TILE;
        }

        // 3. Рисуем стены (прямоугольники из символов WALL_TILE)
        foreach (var wall in walls)
        {
            for (int row = 0; row < wall.Height; row++)
                for (int col = 0; col < wall.Width; col++)
                {
                    int sx = wall.X + col;  // Экранная координата X
                    int sy = wall.Y + row;  // Экранная координата Y
                    if (sx >= 0 && sx < nScreenWidth && sy >= 0 && sy < nScreenHeight)
                        screen[sy * nScreenWidth + sx] = WALL_TILE;
                }
        }

        // 4. Рисуем активные игрушки (только если Active = true)
        foreach (var toy in toys)
        {
            if (toy.Active && toy.X >= 0 && toy.X < nScreenWidth && toy.Y >= 0 && toy.Y < nScreenHeight)
                screen[toy.Y * nScreenWidth + toy.X] = TOY_TILE;
        }

        // 5. Рисуем монстра (его спрайт)
        DrawSprite((int)fMonsterX, (int)fMonsterY, monsterSprite);

        // 6. Рисуем игрока (поверх всего, чтобы перекрывать объекты под ним)
        DrawSprite((int)fPlayerX, (int)fPlayerY, currentSprite);

        // 7. ИНТЕРФЕЙС (UI) - первая строка экрана
        string stateStr = monsterState.ToString();  // Текущее состояние ИИ
        string visibility = CanSeePlayer() ? "CAN SEE" : "HIDDEN";  // Видит ли монстр
        // Статус игрока: [COVERED] = спрятан за стеной присев, [CROUCH] = просто присел, [STANDING] = стоит
        string hideStatus = (bCrouching && IsWallBetween()) ? "[COVERED]" : (bCrouching ? "[CROUCH]" : "[STANDING]");
        string info = $"{hideStatus} Monster:{stateStr}({visibility}) | Toys:{CountActiveToys()} | Pos:{(int)fPlayerX},{(int)fPlayerY} | A/D:Move S:Crouch(Hide) Space:Jump";

        // Копируем строку UI в буфер экрана (символ за символом)
        char[] infoArr = info.ToCharArray();
        for (int i = 0; i < infoArr.Length && i < nScreenWidth; i++) screen[i] = infoArr[i];

        // 8. ВЫВОД НА ЭКРАН
        // Функция WinAPI выводит весь массив screen[] на консоль одним мгновенным вызовом
        WriteConsoleOutputCharacter(hConsole, screen, (uint)(nScreenWidth * nScreenHeight),
            new COORD { X = 0, Y = 0 }, out dwBytesWritten);
    }

    // Счетчик активных (не наступленных) игрушек для UI
    static int CountActiveToys()
    {
        int count = 0;
        foreach (var t in toys) if (t.Active) count++;
        return count;
    }

    // ============================================================================
    // ОТРИСОВКА СПРАЙТА
    // ============================================================================
    // Копирует массив строк (спрайт) в буфер экрана по координатам x,y
    // Пробелы в спрайте считаются прозрачными (не рисуются)
    static void DrawSprite(int x, int y, string[] sprite)
    {
        for (int row = 0; row < sprite.Length; row++)
        {
            int screenY = y + row;
            // Проверка границ экрана по вертикали
            if (screenY < 0 || screenY >= nScreenHeight) continue;

            string line = sprite[row];
            for (int col = 0; col < line.Length; col++)
            {
                int screenX = x + col;
                // Проверка границ по горизонтали
                if (screenX < 0 || screenX >= nScreenWidth) continue;

                char c = line[col];
                // Рисуем только если символ не пробел (прозрачность)
                if (c != ' ')
                    screen[screenY * nScreenWidth + screenX] = c;
            }
        }
    }

    // ============================================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ============================================================================

    // Находит самую длинную строку в спрайте (для расчета ширины коллизии)
    static int GetMaxSpriteWidth(string[] sprite)
    {
        int max = 0;
        foreach (var line in sprite)
            if (line.Length > max) max = line.Length;
        return max;
    }

    // ============================================================================
    // ЭКРАН ЗАВЕРШЕНИЯ ИГРЫ
    // ============================================================================
    // Показывает надпись "GAME OVER" и ждет 3 секунды
    static void ShowGameOver()
    {
        Array.Fill(screen, ' ');  // Очистка

        string msg = "GAME OVER! Monster caught you!";
        int x = (nScreenWidth - msg.Length) / 2;  // Центрирование по X
        int y = nScreenHeight / 2;                 // Центрирование по Y

        // Выводим сообщение посимвольно в центр экрана
        for (int i = 0; i < msg.Length; i++)
            if (x + i >= 0 && x + i < nScreenWidth)
                screen[y * nScreenWidth + x + i] = msg[i];

        WriteConsoleOutputCharacter(hConsole, screen, (uint)(nScreenWidth * nScreenHeight),
            new COORD { X = 0, Y = 0 }, out dwBytesWritten);

        Sleep(3000);  // Пауза 3 секунды перед закрытием
    }
}