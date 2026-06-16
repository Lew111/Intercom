#pragma once

#include <QMainWindow>
#include <QListView>
#include <QScrollArea>
#include <QTextEdit>
#include <QPushButton>
#include <QVBoxLayout>
#include <QToolButton>
#include <QSplitter>
#include <QSettings>
#include <QTimer>
#include <QLineEdit>
#include <QLabel>
#include <QModelIndex>
#include <QDateTime>
#include <QHash>
#include "chatlistmodel.h"
#include "backendmanager.h"

class MessageWidget;
class DaySeparator;
class SmartTextEdit;
class SettingsDialog;

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget* parent = nullptr);
    ~MainWindow();

signals:
    void themeChanged(const QString& theme);

private slots:
    void onChatSelected(const QModelIndex& index);
    void toggleChatList();
    void refreshChats();
    void checkForNewMessages();
    void updateInputForCurrentChat();

    void onBackendStatusClicked();
    void updateBackendStatus();
    void onSendMessageRequested();
    void onBackendReady();
    void onBackendStopped();
    void setupChatCommands();
    void showSettingsDialog();
    void showAboutDialog();
    void loadAndApplyTheme();
    void applyTheme(const QString& theme);

private:
    void saveSettings();
    void loadSettings();

    void scrollToBottom();

    void saveChatOrder();
    void loadChatOrder();
    void applyChatOrder(const QStringList& chatOrder, const QVector<ChatItem>& newChats);

    QString getChatsDirPath() const;
    QString getChatPath(const QString& chatId) const;
    QString getAvatarPath(const QString& chatId) const;
    QString getAlternativeChatPath(const QString& chatId) const;

    QString extractNickFromChatId(const QString& chatId) const;
    QString parseNickFromChatMeta(const QString& chatPath) const;

    void updateThemeForAllWidgets();
    void applyThemeToExistingMessages();

    QLabel* loadingLabel = nullptr;
    QToolButton* toggleChatListButton = nullptr;
    QSplitter* mainSplitter = nullptr;

    QListView* chatList = nullptr;
    ChatListModel* chatModel = nullptr;

    QWidget* messagesContainer = nullptr;
    QVBoxLayout* messagesLayout = nullptr;

    SmartTextEdit* input = nullptr;
    QPushButton* sendButton = nullptr;

    QTimer* refreshTimer = nullptr;
    QString currentChatId;
    QDateTime lastMessageTime;
    QDate currentDisplayedDate;

    QHash<QString, QDateTime> folderModTimes;
    QHash<QString, QDateTime> fileModTimes;
    void checkFilesForChanges();

    void addMessage(const QString& text, bool mine, const QDateTime& time);
    void addDaySeparator(const QDate& date);
    void loadChats(bool forceRefresh = false);
    ChatItem loadChatFromFolder(const QString& chatPath);
    void loadMessagesFromChat(const QString& chatPath, bool checkForNewOnly = false);
    void loadMessagesFromFile(const QString& filePath,
                              QList<QPair<QDateTime, QString>>* newMessages = nullptr,
                              bool loadOnlyNew = false);
    void clearMessages();
    bool hasFolderChanged(const QString& folderPath);
    bool hasFileChanged(const QString& filePath);
    void restoreChatSelection();
    void monitorChatChanges();

    BackendManager* backendManager = nullptr;
    QPushButton* backendStatusButton = nullptr;
    QTimer* backendCheckTimer = nullptr;

    QHash<QString, QStringList> chatSendCommands;
    QHash<QString, QList<int>> chatCommandDelays;

    SettingsDialog* m_settingsDialog = nullptr;
};