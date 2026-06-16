#include "mainwindow.h"
#include "messagewidget.h"
#include "dayseparator.h"
#include "smarttextedit.h"
#include "config.h"
#include "thememanager.h"
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QDateTime>
#include <QCoreApplication>
#include <QDir>
#include <QFile>
#include <QTextStream>
#include <QRegularExpression>
#include <QtGlobal>
#include <QTimer>
#include <QDebug>
#include <QToolButton>
#include <QSplitter>
#include <QSettings>
#include <QApplication>
#include <QScrollBar>
#include <QScrollArea>
#include "chatitemdelegate.h"
#include <QColor> 
#include <utility>
#include <QMimeData>
#include <QByteArray>
#include <QDataStream>
#include <QSysInfo>
#include <QMenuBar>      
#include <QMenu>         
#include <QAction>       
#include "settingsdialog.h"
#include "aboutdialog.h"


QString MainWindow::getChatsDirPath() const
{
    return Config::CHATS_DIR_PATH;
}

QString MainWindow::getChatPath(const QString& chatId) const
{
    return getChatsDirPath() + "/" + chatId;
}

QString MainWindow::getAvatarPath(const QString& chatId) const
{
    return getChatPath(chatId) + "/avatar.png";
}

QString MainWindow::getAlternativeChatPath(const QString& chatId) const
{
    return getChatsDirPath() + "/chat_" + chatId;
}


MainWindow::MainWindow(QWidget* parent)
    : QMainWindow(parent)
    , refreshTimer(new QTimer(this))
{
    setWindowIcon(QIcon(":/new/logo/intercom.png"));    
    loadSettings();
    loadAndApplyTheme();

    backendManager = new BackendManager(this);
    QString backendPath = QCoreApplication::applicationDirPath() +
                          (QSysInfo::productType() == "windows" ? "/InterCom-core.exe" : "/InterCom-core");
    backendManager->setBackendPath(backendPath);
    backendManager->setStartupDelay(2000);

    backendCheckTimer = new QTimer(this);

    auto central = new QWidget;
    setCentralWidget(central);

    auto mainLayout = new QHBoxLayout(central);
    mainLayout->setContentsMargins(0, 0, 0, 0);

    auto leftPanel = new QWidget;
    leftPanel->setMaximumWidth(23);
    auto leftPanelLayout = new QVBoxLayout(leftPanel);
    leftPanelLayout->setContentsMargins(2, 0, 0, 0);
    leftPanelLayout->setSpacing(5);

    backendStatusButton = new QPushButton;
    backendStatusButton->setFixedSize(13, 13);
    backendStatusButton->setCursor(Qt::PointingHandCursor);
    backendStatusButton->setToolTip("Backend status");
    updateBackendStatus(); 

    leftPanelLayout->addWidget(backendStatusButton);

    toggleChatListButton = new QToolButton;
    toggleChatListButton->setText("◀");
    toggleChatListButton->setCheckable(true);
    toggleChatListButton->setChecked(true);
    toggleChatListButton->setFixedSize(20, 30);
    toggleChatListButton->setStyleSheet(
        "QToolButton {"
        "   border: 1px solid #ccc;"
        "   background: #f5f5f5;"
        "   border-radius: 3px;"
        "}"
        "QToolButton:hover {"
        "   background: #e0e0e0;"
        "}"
        );
    leftPanelLayout->addWidget(backendStatusButton);
    leftPanelLayout->addWidget(toggleChatListButton);
    leftPanelLayout->addStretch();


    mainLayout->addWidget(leftPanel);

    QWidget* chatListContainer = new QWidget;
    chatListContainer->setObjectName("chatListContainer");
    chatListContainer->setMinimumWidth(230);
    chatListContainer->setMaximumWidth(400);
    chatListContainer->setAutoFillBackground(true);

    auto chatListLayout = new QVBoxLayout(chatListContainer);
    chatListLayout->setContentsMargins(0, 0, 0, 0);

    auto chatListHeader = new QLabel("Чаты");
    chatListHeader->setObjectName("chatListHeader");
    chatListHeader->setStyleSheet("font-weight: bold; padding: 5px; background: #f0f0f0;");
    chatListHeader->setAlignment(Qt::AlignCenter);

    chatList = new QListView;
    chatList->setObjectName("chatList");
    chatModel = new ChatListModel(this);
    chatList->setModel(chatModel);

    connect(chatModel, &ChatListModel::chatOrderChanged,
            this, &MainWindow::saveChatOrder);

    ChatItemDelegate* delegate = new ChatItemDelegate(chatList);
    chatList->setItemDelegate(delegate);

    chatList->setSelectionMode(QAbstractItemView::SingleSelection);
    chatList->setSelectionBehavior(QAbstractItemView::SelectRows);
    chatList->setFocusPolicy(Qt::NoFocus);

    chatList->setDragEnabled(true);
    chatList->setAcceptDrops(true);
    chatList->setDropIndicatorShown(true);
    chatList->setDragDropMode(QAbstractItemView::InternalMove);
    chatList->setDefaultDropAction(Qt::MoveAction);
    chatList->setDragDropOverwriteMode(false);

    chatList->setProperty("selectionBehavior", "SelectRows");
    chatList->setAttribute(Qt::WA_MacShowFocusRect, false);

   chatList->setStyleSheet("");

    chatListLayout->addWidget(chatList);

    QWidget* rightContainer = new QWidget;
    auto rightLayout = new QVBoxLayout(rightContainer);
    rightLayout->setContentsMargins(0, 0, 0, 0);
    rightLayout->setSpacing(0);

    messagesContainer = new QWidget;
    messagesLayout = new QVBoxLayout(messagesContainer);
    messagesLayout->addStretch();

    loadingLabel = new QLabel("Загрузка...");
    loadingLabel->setAlignment(Qt::AlignCenter);
    loadingLabel->setStyleSheet("color: gray; font-style: italic;");
    loadingLabel->hide();
    messagesLayout->addWidget(loadingLabel);

    QScrollArea* scrollArea = new QScrollArea;
    scrollArea->setWidget(messagesContainer);
    scrollArea->setWidgetResizable(true);
    scrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    rightLayout->addWidget(scrollArea);

    input = new SmartTextEdit;
    input->setPlaceholderText("Введите сообщение...");
    input->setMaxVisibleLines(13);  

    connect(input, &SmartTextEdit::enterPressed,
            this, &MainWindow::onSendMessageRequested);

    sendButton = new QPushButton("➤");
    sendButton->setFixedWidth(40);
    sendButton->setEnabled(false);  

    auto inputLayout = new QHBoxLayout;
    inputLayout->addWidget(input);
    inputLayout->addWidget(sendButton);
    rightLayout->addLayout(inputLayout);

    mainSplitter = new QSplitter(Qt::Horizontal);
    mainSplitter->addWidget(chatListContainer);
    mainSplitter->addWidget(rightContainer);

    mainSplitter->setStretchFactor(0, 1);
    mainSplitter->setStretchFactor(1, 3);

    mainLayout->addWidget(mainSplitter);

    if (this->size().isEmpty()) {
        resize(1000, 700);
    }

    connect(chatList, &QListView::clicked,
            this, &MainWindow::onChatSelected);
    connect(toggleChatListButton, &QToolButton::toggled,
            this, &MainWindow::toggleChatList);

    connect(backendStatusButton, &QPushButton::clicked,
            this, &MainWindow::onBackendStatusClicked);
    connect(backendManager, &BackendManager::runningStateChanged,
            this, &MainWindow::updateBackendStatus);
    connect(backendManager, &BackendManager::readyStateChanged,
            this, &MainWindow::updateBackendStatus);
    connect(backendManager, &BackendManager::backendStarted,
            this, &MainWindow::onBackendReady);
    connect(backendManager, &BackendManager::backendStopped,
            this, &MainWindow::onBackendStopped);
    connect(backendCheckTimer, &QTimer::timeout,
            this, &MainWindow::updateBackendStatus);

    connect(sendButton, &QPushButton::clicked,
            this, &MainWindow::onSendMessageRequested);

    connect(refreshTimer, &QTimer::timeout, this, &MainWindow::monitorChatChanges);
    refreshTimer->start(1000);
    connect(backendManager, &BackendManager::consoleOutput, this, [this](const QString& text) {
        if (m_settingsDialog) {
            m_settingsDialog->appendConsoleOutput(text);
        }
    });

    qDebug() << "Timer started with interval:" << refreshTimer->interval() << "ms";

    loadChats();

    setupChatCommands();

    backendCheckTimer->start(1000);

    QTimer::singleShot(1000, backendManager, &BackendManager::startBackend);

    if (!currentChatId.isEmpty() && chatModel) {
        for (int i = 0; i < chatModel->rowCount(QModelIndex()); ++i) {
            QModelIndex idx = chatModel->index(i, 0);
            QString chatId = idx.data(ChatListModel::ChatIdRole).toString();
            if (chatId == currentChatId) {
                chatList->setCurrentIndex(idx);
                qDebug() << "Restored selection for chat:" << currentChatId;
                break;
            }
        }
    }
    QTimer::singleShot(100, this, [this]() {
        if (!currentChatId.isEmpty()) {
            restoreChatSelection();
        }
    });

    updateInputForCurrentChat();
    QMenuBar* menuBar = new QMenuBar(this);
    this->setMenuBar(menuBar);

    QMenu* fileMenu = menuBar->addMenu("Файл");
    QAction* exitAction = fileMenu->addAction("Выход");
    connect(exitAction, &QAction::triggered, this, &QWidget::close);

    QMenu* settingsMenu = menuBar->addMenu("Настройки");
    QAction* settingsAction = settingsMenu->addAction("Настройки");
    connect(settingsAction, &QAction::triggered, this, &MainWindow::showSettingsDialog);

    QMenu* helpMenu = menuBar->addMenu("Справка");
    QAction* aboutAction = helpMenu->addAction("О программе");
    connect(aboutAction, &QAction::triggered, this, &MainWindow::showAboutDialog);
    loadAndApplyTheme();
}

MainWindow::~MainWindow()
{
    saveChatOrder();
    saveSettings();
    if (backendManager) {
        backendManager->stopBackend();
    }
}

void MainWindow::loadAndApplyTheme()
{
    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);  
    QString theme = settings.value("theme", "light").toString();
    qDebug() << "Loading theme from settings:" << theme;
    qDebug() << "Settings file:" << settings.fileName();

    applyTheme(theme);
}

void MainWindow::saveSettings()
{
    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);  
    settings.setValue("geometry", saveGeometry());
    settings.setValue("windowState", saveState());
    if (mainSplitter) {
        settings.setValue("splitterState", mainSplitter->saveState());
    }
    if (toggleChatListButton) {
        settings.setValue("chatListVisible", toggleChatListButton->isChecked());
    }
    settings.setValue("currentChat", currentChatId);

    
    ThemeManager::Theme currentTheme = ThemeManager::instance().getCurrentTheme();
    settings.setValue("theme", currentTheme == ThemeManager::Dark ? "dark" : "light");

    saveChatOrder();

    if (backendManager) {
        settings.setValue("backendPath", backendManager->getBackendPath());
    }

    
    if (!currentChatId.isEmpty() && input) {
        QString text = input->toPlainText();
        settings.setValue("input_" + currentChatId, text);
        qDebug() << "Saved text for chat" << currentChatId << ":" << text.left(20);
    }

    
    settings.sync();

    qDebug() << "Settings saved to:" << settings.fileName();
    qDebug() << "Window geometry saved:" << saveGeometry().toHex();
}

void MainWindow::loadSettings()
{
    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);

    QByteArray geometry = settings.value("geometry").toByteArray();
    if (!geometry.isEmpty()) {
        restoreGeometry(geometry);
        qDebug() << "Window geometry restored:" << geometry.toHex();
    } else {
        qDebug() << "No saved geometry found";
    }

    restoreState(settings.value("windowState").toByteArray());

    bool chatListVisible = settings.value("chatListVisible", true).toBool();
    if (toggleChatListButton) {
        toggleChatListButton->setChecked(chatListVisible);
    }

    currentChatId = settings.value("currentChat").toString();

    if (backendManager) {
        QString backendPath = settings.value("backendPath",
                                             QCoreApplication::applicationDirPath() + "/backend.exe").toString();
        backendManager->setBackendPath(backendPath);
    }
}

void MainWindow::toggleChatList()
{
    if (!toggleChatListButton || !mainSplitter) return;

    bool visible = toggleChatListButton->isChecked();
    toggleChatListButton->setText(visible ? "◀" : "▶");

    if (visible) {
        mainSplitter->widget(0)->show();
    } else {
        mainSplitter->widget(0)->hide();
    }
}

void MainWindow::refreshChats()
{
    loadChats(true); 
}

void MainWindow::checkForNewMessages()
{
    if (!currentChatId.isEmpty()) {
        QString chatPath = getChatPath(currentChatId);

        QDir dir(chatPath);
        if (!dir.exists()) {
            qDebug() << "Chat path does not exist:" << chatPath;
            return;
        }

        qDebug() << "=== CHECKING FOR NEW MESSAGES ===";

        
        static QDateTime lastFolderCheck;
        QFileInfo folderInfo(chatPath);
        QDateTime currentModTime = folderInfo.lastModified();

        if (!lastFolderCheck.isValid() || currentModTime > lastFolderCheck) {
            qDebug() << "Chat folder modified, reloading messages";
            lastFolderCheck = currentModTime;

            loadMessagesFromChat(chatPath, false); 
        } else {
            qDebug() << "No changes in chat folder";
        }
    }
}

void MainWindow::saveChatOrder()
{
    if (!chatModel) return;

    QStringList chatOrder;
    for (int i = 0; i < chatModel->rowCount(QModelIndex()); ++i) {
        QModelIndex idx = chatModel->index(i, 0);
        QString chatId = idx.data(ChatListModel::ChatIdRole).toString();
        chatOrder.append(chatId);
    }

    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
    settings.setValue("chatOrder", chatOrder);
    settings.sync();  

    qDebug() << "Saved chat order:" << chatOrder;
}

void MainWindow::loadChatOrder()
{

    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
    QStringList savedOrder = settings.value("chatOrder").toStringList();

    if (!savedOrder.isEmpty()) {
        qDebug() << "Loaded saved chat order:" << savedOrder;
    }
}

void MainWindow::applyChatOrder(const QStringList& chatOrder, const QVector<ChatItem>& newChats)
{
    if (!chatModel) return;

    QVector<ChatItem> chatsToSort = newChats;
    QVector<ChatItem> orderedChats;

    for (const QString& chatId : chatOrder) {
        auto it = std::find_if(chatsToSort.begin(), chatsToSort.end(),
                               [&chatId](const ChatItem& item) {
                                   return item.chatId == chatId;
                               });

        if (it != chatsToSort.end()) {
            orderedChats.append(*it);
            chatsToSort.erase(it);
        }
    }

    orderedChats.append(chatsToSort);

    chatModel->setChats(orderedChats);

    qDebug() << "Applied chat order, total chats:" << orderedChats.size();
}

void MainWindow::loadChats(bool forceRefresh)
{
    static QDateTime lastCheck;
    QDateTime now = QDateTime::currentDateTime();

    if (!forceRefresh && lastCheck.isValid() && lastCheck.msecsTo(now) < 500) {
        return;
    }

    lastCheck = now;

    QDir chatsDir(getChatsDirPath());
    if (!chatsDir.exists()) {
        qDebug() << "Chats directory does not exist:" << getChatsDirPath();
        return;
    }

    QStringList chatFolders = chatsDir.entryList(QDir::Dirs | QDir::NoDotAndDotDot);
    QVector<ChatItem> chats;
    bool hasChanges = false;

    for (const QString& folder : std::as_const(chatFolders)) {
        QString folderPath = chatsDir.filePath(folder);
        QFileInfo folderInfo(folderPath);
        QDateTime modTime = folderInfo.lastModified();

        if (!folderModTimes.contains(folder) || modTime > folderModTimes[folder]) {
            hasChanges = true;
            folderModTimes[folder] = modTime;
        }

        ChatItem item = loadChatFromFolder(folderPath);
        if (!item.chatId.isEmpty()) {
            chats.append(item);
        }
    }

    if (hasChanges || forceRefresh) {
        QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
        QStringList savedOrder = settings.value("chatOrder").toStringList();

        if (!savedOrder.isEmpty() && !chats.isEmpty()) {
            applyChatOrder(savedOrder, chats);
        } else {
            std::sort(chats.begin(), chats.end(),
                      [](const ChatItem& a, const ChatItem& b) {
                          return a.lastTime > b.lastTime;
                      });
            chatModel->setChats(chats);
        }

        if (!currentChatId.isEmpty()) {
            QTimer::singleShot(0, this, &MainWindow::restoreChatSelection);
        }
    }
}

ChatItem MainWindow::loadChatFromFolder(const QString& chatPath)
{
    ChatItem item;
    QDir dir(chatPath);

    if (!dir.exists()) {
        qDebug() << "Chat folder does not exist:" << chatPath;
        return item;
    }

    QFile metaFile(dir.filePath("chat.md"));
    if (!metaFile.open(QIODevice::ReadOnly | QIODevice::Text)) {
        qDebug() << "Cannot open chat.md in:" << chatPath;
        return item;
    }

    QTextStream meta(&metaFile);
    while (!meta.atEnd()) {
        QString line = meta.readLine();
        if (line.startsWith("id:")) {
            item.chatId = line.mid(3).trimmed();
            qDebug() << "Loaded chatId:" << item.chatId << "from:" << chatPath;
        }
        if (line.startsWith("title:")) {
            item.title = line.mid(6).trimmed();
        }
    }
    metaFile.close();

    QStringList days = dir.entryList(QStringList() << "*.md", QDir::Files);
    days.removeAll("chat.md");

    if (days.isEmpty()) {
        qDebug() << "No message files in:" << chatPath;
        return item;
    }

    days.sort();
    QString lastDayFile = days.last();

    QFile dayFile(dir.filePath(lastDayFile));
    if (!dayFile.open(QIODevice::ReadOnly | QIODevice::Text)) {
        qDebug() << "Cannot open day file:" << lastDayFile;
        return item;
    }

    QTextStream in(&dayFile);
    QString lastText;
    QString lastHeader;
    QDateTime lastTime;

    QRegularExpression timeRe(R"(## (\d\d:\d\d))");

    while (!in.atEnd()) {
        QString line = in.readLine();
        if (line.startsWith("## ")) {
            lastHeader = line;
            lastText.clear();

            QRegularExpressionMatch m = timeRe.match(lastHeader);
            if (m.hasMatch()) {
                QTime time = QTime::fromString(m.captured(1), "HH:mm");
                QDate date = QDate::fromString(lastDayFile.left(10), "yyyy-MM-dd");
                lastTime = QDateTime(date, time);
            }
        } else if (!line.trimmed().isEmpty() && lastText.isEmpty()) {
            lastText = line;
        }
    }

    dayFile.close();

    item.lastMessage = lastText;
    item.lastTime = lastTime;

    return item;
}

void MainWindow::onChatSelected(const QModelIndex& index)
{
    if (!index.isValid()) return;

    QString chatId = index.data(ChatListModel::ChatIdRole).toString();
    if (chatId.isEmpty()) {
        qDebug() << "Empty chatId selected";
        return;
    }

    if (currentChatId == chatId) {
        qDebug() << "Same chat selected, skipping";

        if (chatList) {
            chatList->setCurrentIndex(index);
        }
        return;
    }

    qDebug() << "=== Switching from chat" << currentChatId << "to" << chatId << "===";

    if (chatList) {
        chatList->setCurrentIndex(index);
    }

    if (!currentChatId.isEmpty() && input) {
        QString currentText = input->toPlainText();
        QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
        settings.setValue("input_" + currentChatId, currentText);
        qDebug() << "Saved text for chat" << currentChatId
                 << ":" << currentText.left(20);
    }

    currentChatId = chatId;
    QString chatPath =  getChatPath(chatId);

    QDir dir(chatPath);
    if (!dir.exists()) {
        qDebug() << "Selected chat folder does not exist:" << chatPath;
        QString alternativePath = getAlternativeChatPath(chatId);
        QDir altDir(alternativePath);
        if (altDir.exists()) {
            qDebug() << "Found alternative path:" << alternativePath;
            chatPath = alternativePath;
        } else {
            loadingLabel->setText("Чат не найден");
            loadingLabel->show();
            QTimer::singleShot(2000, loadingLabel, &QLabel::hide);
            return;
        }
    }

    updateInputForCurrentChat();

    clearMessages();
    if (loadingLabel) {
        loadingLabel->show();
    }

    lastMessageTime = QDateTime();

    fileModTimes.clear();

    QTimer::singleShot(100, this, [this, chatPath]() {
        loadMessagesFromChat(chatPath, false);
        if (loadingLabel) {
            loadingLabel->hide();
        }

        
    });
}

void MainWindow::clearMessages()
{
    if (!messagesLayout) return;

    while (messagesLayout->count() > 0) {
        QLayoutItem* item = messagesLayout->takeAt(0);
        if (item && item->widget() && item->widget() != loadingLabel) {
            delete item->widget();
        }
        delete item;
    }

    messagesLayout->addStretch();
    if (loadingLabel && messagesLayout->indexOf(loadingLabel) == -1) {
        messagesLayout->addWidget(loadingLabel);
    }
    if (loadingLabel) {
        loadingLabel->hide();
    }

    currentDisplayedDate = QDate(); 
}

void MainWindow::addDaySeparator(const QDate& date)
{
    if (!messagesLayout || date == currentDisplayedDate) {
        return;
    }

    currentDisplayedDate = date;

    int insertIndex = messagesLayout->count() - 1;
    if (insertIndex < 0) insertIndex = 0;

    auto separator = new DaySeparator(date);
    messagesLayout->insertWidget(insertIndex, separator);
}

void MainWindow::addMessage(const QString& text, bool mine, const QDateTime& time)
{
    if (!messagesLayout) return;

    qDebug() << "==== ADD MESSAGE CALLED ====";
    qDebug() << "Text:" << text.left(50);
    qDebug() << "Mine:" << mine;
    qDebug() << "Time:" << time;

    addDaySeparator(time.date());

    int insertIndex = messagesLayout->count() - 1;
    if (insertIndex < 0) insertIndex = 0;

    MessageWidget* w = new MessageWidget(text, mine, time);

    ThemeManager::Theme currentTheme = ThemeManager::instance().getCurrentTheme();
    QString themeStr;
    switch (currentTheme) {
    case ThemeManager::Light:
        themeStr = "light";
        break;
    case ThemeManager::Dark:
        themeStr = "dark";
        break;
    case ThemeManager::System:
        themeStr = "system";
        break;
    default:
        themeStr = "modern";
        break;
    }
    w->setMessageStyle(themeStr);

    messagesLayout->insertWidget(insertIndex, w);

    if (!lastMessageTime.isValid() || time > lastMessageTime) {
        lastMessageTime = time;
    }

    messagesLayout->update();
    messagesContainer->update();
    scrollToBottom();
}

bool MainWindow::hasFolderChanged(const QString& folderPath)
{
    QFileInfo folderInfo(folderPath);
    if (!folderInfo.exists()) return false;

    QDateTime currentModTime = folderInfo.lastModified();

    if (!folderModTimes.contains(folderPath) || currentModTime > folderModTimes[folderPath]) {
        folderModTimes[folderPath] = currentModTime;
        return true;
    }

    return false;
}

bool MainWindow::hasFileChanged(const QString& filePath)
{
    QFileInfo fileInfo(filePath);
    if (!fileInfo.exists()) return false;

    QDateTime currentModTime = fileInfo.lastModified();

    if (!fileModTimes.contains(filePath) || currentModTime > fileModTimes[filePath]) {
        fileModTimes[filePath] = currentModTime;
        return true;
    }

    return false;
}


void MainWindow::monitorChatChanges()
{
    qDebug() << "=== MONITOR CHANGES CALLED ===";

    static QDateTime lastChatCheck;
    QDateTime now = QDateTime::currentDateTime();

    if (!lastChatCheck.isValid() || lastChatCheck.msecsTo(now) > 3000) {
        loadChats(true);
        lastChatCheck = now;
    }

    if (!currentChatId.isEmpty()) {
        checkFilesForChanges();
    }
}

void MainWindow::loadMessagesFromChat(const QString& chatPath, bool checkForNewOnly)
{
    QDir dir(chatPath);
    if (!dir.exists()) {
        qDebug() << "Chat path does not exist:" << chatPath;
        return;
    }

    QStringList days = dir.entryList(QStringList() << "*.md", QDir::Files);
    days.removeAll("chat.md");
    days.sort();

    if (checkForNewOnly) {
        QList<QPair<QDateTime, QString>> newMessages;

        for (const QString& dayFile : std::as_const(days)) {
            QString filePath = dir.filePath(dayFile);

            if (hasFileChanged(filePath) || !fileModTimes.contains(filePath)) {
                loadMessagesFromFile(filePath, &newMessages, true);
                qDebug() << "File changed:" << dayFile;
            }
        }

        int addedCount = 0;
        for (const auto& msg : std::as_const(newMessages)) {
            if (msg.first > lastMessageTime) {
                addMessage(msg.second, false, msg.first);
                addedCount++;
            }
        }

        if (addedCount > 0) {
            qDebug() << "Added" << addedCount << "new messages";

            messagesContainer->adjustSize();

            scrollToBottom();
        }
    } else {
        clearMessages();

        for (const QString& dayFile : std::as_const(days)) {
            loadMessagesFromFile(dir.filePath(dayFile));
        }

        messagesLayout->update();
        messagesContainer->updateGeometry();
        messagesContainer->adjustSize();

        QApplication::processEvents();

        scrollToBottom();
    }
}

void MainWindow::scrollToBottom()
{
    if (!messagesContainer || !messagesContainer->parentWidget()) {
        return;
    }

    QWidget* parent = messagesContainer->parentWidget();
    if (!parent) return;

    QScrollArea* scrollArea = qobject_cast<QScrollArea*>(parent->parentWidget());
    if (!scrollArea) return;

    QTimer::singleShot(0, [scrollArea]() {
        QApplication::processEvents();

        QTimer::singleShot(10, [scrollArea]() {
            if (scrollArea && scrollArea->verticalScrollBar()) {
                int max = scrollArea->verticalScrollBar()->maximum();
                qDebug() << "Scrolling to maximum:" << max;
                scrollArea->verticalScrollBar()->setValue(max);

                QTimer::singleShot(50, [scrollArea]() {
                    if (scrollArea && scrollArea->verticalScrollBar()) {
                        int current = scrollArea->verticalScrollBar()->value();
                        int max = scrollArea->verticalScrollBar()->maximum();
                        if (current < max) {
                            scrollArea->verticalScrollBar()->setValue(max);
                        }
                    }
                });
            }
        });
    });
}


void MainWindow::loadMessagesFromFile(const QString& filePath,QList<QPair<QDateTime, QString>>* newMessages,bool loadOnlyNew)
{
    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) {
        qDebug() << "Cannot open file:" << filePath;
        return;
    }

    QTextStream in(&file);
    QString currentText;
    bool currentMine = false;
    QDateTime currentTime;

    QRegularExpression headerRe(R"(##\s*(\d{1,2}:\d{2})\s*[|-]?\s*(\w+))");

    qDebug() << "Loading file:" << filePath << "loadOnlyNew:" << loadOnlyNew;

    while (!in.atEnd()) {
        QString line = in.readLine().trimmed();

        if (line.isEmpty()) continue;

        QRegularExpressionMatch m = headerRe.match(line);
        if (m.hasMatch()) {
            if (!currentText.isEmpty()) {
                QString trimmedText = currentText.trimmed();
                if (!trimmedText.isEmpty()) {
                    if (newMessages) {
                        newMessages->append(qMakePair(currentTime, trimmedText));
                    } else {
                        qDebug() << "Adding message:" << trimmedText.left(30)
                                 << "time:" << currentTime << "mine:" << currentMine;
                        addMessage(trimmedText, currentMine, currentTime);
                    }
                }
                currentText.clear();
            }

            QString timeStr = m.captured(1);
            QString author = m.captured(2);

            QTime time = QTime::fromString(timeStr, "HH:mm");
            if (!time.isValid()) {
                time = QTime::fromString(timeStr, "H:mm");
            }

            QFileInfo fi(filePath);
            QString fileName = fi.baseName(); 

            QDate date;
            if (fileName.contains("_")) {
                QString dateStr = fileName.section('_', -1); 
                date = QDate::fromString(dateStr, "yyyy-MM-dd");
            }

            if (!date.isValid()) {
                date = QDate::currentDate(); 
            }

            currentTime = QDateTime(date, time);

            QString authorLower = author.toLower();
            currentMine = (authorLower == "me" || authorLower == "я" ||
                           authorLower == "user" || authorLower == "you");

            qDebug() << "Parsed header - time:" << currentTime
                     << "author:" << author << "mine:" << currentMine;

        } else if (!line.startsWith("## ")) {
            currentText += line + "\n";
        }
    }

    if (!currentText.isEmpty()) {
        QString trimmedText = currentText.trimmed();
        if (!trimmedText.isEmpty()) {
            if (newMessages) {
                newMessages->append(qMakePair(currentTime, trimmedText));
            } else {
                qDebug() << "Adding last message:" << trimmedText.left(30)
                << "time:" << currentTime << "mine:" << currentMine;
                addMessage(trimmedText, currentMine, currentTime);
            }
        }
    }

    file.close();
}

void MainWindow::checkFilesForChanges()
{
    if (!currentChatId.isEmpty()) {
        QString chatPath = getChatPath(currentChatId);

        QDir dir(chatPath);
        if (!dir.exists()) return;

        QStringList days = dir.entryList(QStringList() << "*.md", QDir::Files);
        days.removeAll("chat.md");

        bool needsReload = false;

        for (const QString& dayFile : std::as_const(days)) {
            QString filePath = dir.filePath(dayFile);
            QFileInfo fileInfo(filePath);

            if (!fileModTimes.contains(filePath)) {
                fileModTimes[filePath] = fileInfo.lastModified();
                needsReload = true;
                qDebug() << "New file detected:" << dayFile;
            } else if (fileInfo.lastModified() > fileModTimes[filePath]) {
                fileModTimes[filePath] = fileInfo.lastModified();
                needsReload = true;
                qDebug() << "File modified:" << dayFile;
            }
        }

        if (needsReload) {
            qDebug() << "Reloading messages due to file changes";
            loadMessagesFromChat(chatPath, false);
        }
    }
}

void MainWindow::updateInputForCurrentChat()
{
    if (input && !currentChatId.isEmpty()) {
        QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
        QString cachedText = settings.value("input_" + currentChatId, "").toString();
        qDebug() << "Loading text for chat" << currentChatId
                 << ":" << cachedText.left(20);

        input->setPlainText(cachedText);
    } else if (input) {
        input->clear();
    }
}

void MainWindow::restoreChatSelection()
{
    if (currentChatId.isEmpty() || !chatModel || !chatList) return;

    qDebug() << "Trying to restore selection for chat:" << currentChatId;

    for (int i = 0; i < chatModel->rowCount(QModelIndex()); ++i) {
        QModelIndex idx = chatModel->index(i, 0);
        QString chatId = idx.data(ChatListModel::ChatIdRole).toString();

        if (chatId == currentChatId) {
            chatList->blockSignals(true);
            chatList->setCurrentIndex(idx);
            chatList->blockSignals(false);

            chatList->selectionModel()->select(idx, QItemSelectionModel::Select | QItemSelectionModel::Rows);

            qDebug() << "Successfully restored selection for chat:" << currentChatId << "at row" << i;
            return;
        }
    }

    qDebug() << "Failed to restore selection for chat:" << currentChatId;
}


void MainWindow::onBackendStatusClicked()
{
    if (backendManager) {
        qDebug() << "Backend status button clicked - restarting";
        backendManager->restartBackend();
    }
}

void MainWindow::updateBackendStatus()
{
    if (!backendStatusButton || !backendManager) return;

    bool isRunning = backendManager->isRunning();
    bool isReady = backendManager->isReady();

    QString color;
    QString tooltip;

    if (!isRunning) {
        color = "#ff4444"; 
        tooltip = "Backend stopped\nClick to start";
    } else if (!isReady) {
        color = "#ffaa44"; 
        tooltip = "Backend starting...\nClick to restart";
    } else {
        color = "#44ff44"; 
        tooltip = "Backend running\nClick to restart";
    }

    QColor baseColor(color);
    QColor darkerColor = baseColor.darker(120);

    QString style = QString(
                        "QPushButton {"
                        "   background-color: %1;"
                        "   border-radius: 15px;"
                        "   border: 2px solid #ffffff;"
                        "}"
                        "QPushButton:hover {"
                        "   border: 2px solid #000000;"
                        "}"
                        "QPushButton:pressed {"
                        "   background-color: %2;"
                        "}"
                        ).arg(color, darkerColor.name());

    backendStatusButton->setStyleSheet(style);
    backendStatusButton->setToolTip(tooltip);

    if (sendButton) {
        sendButton->setEnabled(isReady);
        if (isReady) {
            sendButton->setToolTip("Send message");
        } else {
            sendButton->setToolTip("Backend not ready");
        }
    }
}

void MainWindow::onSendMessageRequested()
{
    if (!backendManager || !backendManager->isReady() || currentChatId.isEmpty()) {
        qDebug() << "Cannot send message - backend not ready or no chat selected";
        return;
    }

    QString message = input->toPlainText().trimmed();
    if (message.isEmpty()) {
        return;
    }

    QString nick;
    QString chatPath = getChatPath(currentChatId);
    QDir dir(chatPath);

    if (dir.exists()) {
        nick = parseNickFromChatMeta(chatPath);
        qDebug() << "Nick from chat.md:" << nick;
    }

    if (nick.isEmpty()) {
        nick = extractNickFromChatId(currentChatId);
        qDebug() << "Nick from chat ID:" << nick;
    }

    if (nick.isEmpty()) {
        qWarning() << "Could not determine nick for chat:" << currentChatId;
        nick = "unknown";
    }

    qDebug() << "Final nick:" << nick << "Chat ID:" << currentChatId;
    qDebug() << "Message:" << message;

    QString command = QString("send %1 %2").arg(nick).arg(message);

    qDebug() << "Command to send:" << command;

    QStringList allCommands;
    allCommands << command;

    QList<int> allDelays;
    allDelays << 0;

    backendManager->sendCommands(allCommands, allDelays);

    input->clear();
    input->update();

    if (!currentChatId.isEmpty() && input) {
        QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);
        settings.setValue("input_" + currentChatId, "");
    }
}

void MainWindow::onBackendReady()
{
    qDebug() << "Backend is ready, can send messages now";
    if (sendButton) {
        sendButton->setEnabled(true);
        sendButton->setToolTip("Send message");
    }
}

void MainWindow::onBackendStopped()
{
    qDebug() << "Backend stopped";
    if (sendButton) {
        sendButton->setEnabled(false);
        sendButton->setToolTip("Backend not running");
    }
}

void MainWindow::setupChatCommands()
{
    QStringList defaultCommands;

    QList<int> defaultDelays;

    chatSendCommands["default"] = defaultCommands;
    chatCommandDelays["default"] = defaultDelays;

    qDebug() << "Chat commands setup complete";
}

QString MainWindow::extractNickFromChatId(const QString& chatId) const
{
    
    QString nick = chatId;
    if (nick.startsWith("chat_")) {
        nick = nick.mid(5);
    }

    return nick;
}

QString MainWindow::parseNickFromChatMeta(const QString& chatPath) const
{
    QFile metaFile(chatPath + "/chat.md");
    if (!metaFile.open(QIODevice::ReadOnly | QIODevice::Text)) {
        qDebug() << "Cannot open chat.md in:" << chatPath;
        return QString();
    }

    QTextStream in(&metaFile);
    QString nick;

    while (!in.atEnd()) {
        QString line = in.readLine().trimmed();

        if (line.startsWith("nick:")) {
            int colonIndex = line.indexOf(':');
            if (colonIndex != -1) {
                nick = line.mid(colonIndex + 1).trimmed();
                qDebug() << "Found nick in chat.md:" << nick;
                break;
            }
        }
        if (line.startsWith("username:")) {
            int colonIndex = line.indexOf(':');
            if (colonIndex != -1) {
                nick = line.mid(colonIndex + 1).trimmed();
                qDebug() << "Found username in chat.md:" << nick;
                break;
            }
        }
    }

    metaFile.close();

    if (nick.isEmpty()) {
        qDebug() << "No nick found in chat.md";
    }

    return nick;
}

void MainWindow::showSettingsDialog()
{
    if (!m_settingsDialog) {
        m_settingsDialog = new SettingsDialog(this);

        connect(m_settingsDialog, &SettingsDialog::themeChanged, this, [this](const QString& theme) {
            applyTheme(theme);
        });

        connect(m_settingsDialog, &SettingsDialog::consoleCommandEntered,
                this, [this](const QString& command) {
                    if (backendManager) {
                        backendManager->sendCommand(command);
                    }
                });

        if (backendManager && backendManager->getProcess()) {
            m_settingsDialog->setBackendProcess(backendManager->getProcess());
        }

        connect(m_settingsDialog, &QDialog::destroyed, this, [this]() {
            m_settingsDialog = nullptr;
        });
    }

    m_settingsDialog->show();
    m_settingsDialog->raise();
    m_settingsDialog->activateWindow();
}
void MainWindow::showAboutDialog()
{
    AboutDialog* dialog = new AboutDialog(this);
    dialog->show();
}

void MainWindow::applyTheme(const QString& theme)
{
    qDebug() << "Applying theme:" << theme;

    QSettings settings(Config::ORGANIZATION_NAME, Config::APPLICATION_NAME);  
    settings.setValue("theme", theme);
    settings.sync();
    qDebug() << "Theme saved to settings:" << theme;

    QString actualTheme = theme;
    if (actualTheme != "dark") {
        actualTheme = "light";  
    }

    if (actualTheme == "dark") {
        ThemeManager::instance().applyTheme(ThemeManager::Dark, this);
    } else {
        ThemeManager::instance().applyTheme(ThemeManager::Light, this);
    }

    QWidget* chatListContainer = findChild<QWidget*>("chatListContainer");
    if (chatListContainer) {
        QPalette pal = chatListContainer->palette();
        if (actualTheme == "dark") {
            pal.setColor(QPalette::Window, QColor(43, 43, 43));
        } else {
            pal.setColor(QPalette::Window, Qt::white);
        }
        chatListContainer->setPalette(pal);
        chatListContainer->setAutoFillBackground(true);
    }

    QLabel* chatListHeader = findChild<QLabel*>("chatListHeader");
    if (chatListHeader) {
        if (actualTheme == "dark") {
            chatListHeader->setStyleSheet("font-weight: bold; padding: 5px; background: #3c3c3c; color: #ffffff;");
        } else {
            chatListHeader->setStyleSheet("font-weight: bold; padding: 5px; background: #f0f0f0; color: #333333;");
        }
    }

    if (actualTheme == "dark") {
        qApp->setStyleSheet(
            "QWidget { background-color: #2b2b2b; color: #ffffff; }"
            "QMainWindow { background-color: #2b2b2b; }"
            "QMenuBar { background-color: #3c3c3c; color: #ffffff; border: none; }"
            "QMenuBar::item:selected { background-color: #4a4a4a; }"
            "QMenu { background-color: #3c3c3c; color: #ffffff; border: 1px solid #5a5a5a; }"
            "QMenu::item:selected { background-color: #4a4a4a; }"
            "QListView { background-color: #2b2b2b; border: none; outline: none; }"
            "QListView::item { background-color: #2b2b2b; border: none; padding: 0px; }"
            "QListView::item:selected { background-color: #34495e; }"
            "QListView::item:hover { background-color: #3a3a3a; }"
            "QTextEdit { background-color: #3c3c3c; color: #ffffff; border: 1px solid #555; border-radius: 5px; }"
            "QPushButton { background-color: #4e9cff; color: white; border: none; border-radius: 4px; padding: 5px; }"
            "QPushButton:hover { background-color: #3d8cff; }"
            "QPushButton:disabled { background-color: #666; }"
            "QLabel { color: #ffffff; }"
            "QGroupBox { color: #ffffff; border: 1px solid #555; margin-top: 10px; }"
            "QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; }"
            "QScrollBar:vertical { background: #2b2b2b; width: 10px; border-radius: 5px; }"
            "QScrollBar::handle:vertical { background: #5a5a5a; border-radius: 5px; min-height: 20px; }"
            "QScrollBar::handle:vertical:hover { background: #7a7a7a; }"
            "QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { border: none; background: none; }"
            "QSplitter::handle { background-color: #3c3c3c; }"
            );
    } else {
        qApp->setStyleSheet(
            "QWidget { background-color: #f5f5f5; color: #333333; }"
            "QMainWindow { background-color: #f5f5f5; }"
            "QMenuBar { background-color: #ffffff; color: #333333; border: none; border-bottom: 1px solid #ddd; }"
            "QMenuBar::item:selected { background-color: #e0e0e0; }"
            "QMenu { background-color: #ffffff; color: #333333; border: 1px solid #ccc; }"
            "QMenu::item:selected { background-color: #e0e0e0; }"
            "QListView { background-color: white; border: none; outline: none; }"
            "QListView::item { background-color: white; border: none; padding: 0px; }"
            "QListView::item:selected { background-color: #e3f2fd; }"
            "QListView::item:hover { background-color: #f0f0f0; }"
            "QTextEdit { background-color: white; color: #333333; border: 1px solid #ddd; border-radius: 5px; }"
            "QPushButton { background-color: #4e9cff; color: white; border: none; border-radius: 4px; padding: 5px; }"
            "QPushButton:hover { background-color: #3d8cff; }"
            "QPushButton:disabled { background-color: #ccc; }"
            "QLabel { color: #333333; }"
            "QGroupBox { color: #333333; border: 1px solid #ddd; margin-top: 10px; }"
            "QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 5px; }"
            "QScrollBar:vertical { background: #f0f0f0; width: 10px; border-radius: 5px; }"
            "QScrollBar::handle:vertical { background: #c0c0c0; border-radius: 5px; min-height: 20px; }"
            "QScrollBar::handle:vertical:hover { background: #a0a0a0; }"
            "QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { border: none; background: none; }"
            "QSplitter::handle { background-color: #e0e0e0; }"
            );
    }

    applyThemeToExistingMessages();

    if (chatList) {
        chatList->viewport()->update();
        chatList->update();
    }

    emit themeChanged(theme);
}

void MainWindow::applyThemeToExistingMessages()
{
    if (!messagesLayout) return;

    ThemeManager::Theme currentTheme = ThemeManager::instance().getCurrentTheme();

    for (int i = 0; i < messagesLayout->count(); ++i) {
        QLayoutItem* item = messagesLayout->itemAt(i);
        if (!item || !item->widget()) continue;

        QWidget* widget = item->widget();

        if (MessageWidget* msgWidget = qobject_cast<MessageWidget*>(widget)) {
            msgWidget->updateTheme();
        }
        else if (DaySeparator* sepWidget = qobject_cast<DaySeparator*>(widget)) {
            sepWidget->updateTheme();
        }
    }
}