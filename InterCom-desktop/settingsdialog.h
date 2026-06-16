#ifndef SETTINGSDIALOG_H
#define SETTINGSDIALOG_H

#include <QDialog>
#include <QComboBox>
#include <QLineEdit>
#include <QLabel>
#include <QPushButton>
#include <QTabWidget>
#include <QTextEdit>
#include <QProcess>
#include <QCheckBox>

class SettingsDialog : public QDialog
{
    Q_OBJECT

public:
    explicit SettingsDialog(QWidget* parent = nullptr);
    ~SettingsDialog();

    void appendConsoleOutput(const QString& text);
    void setBackendProcess(QProcess* process);

signals:
    void themeChanged(const QString& theme);
    void consoleCommandEntered(const QString& command);

private slots:
    void saveSettings();
    void onThemeChanged();
    void applyNickname();
    void onNicknameTextChanged(const QString& text);
    void sendConsoleCommand();
    void clearConsole();

private:
    void setupUI();
    void loadSettings();
    void loadNickname();
    bool isValidNickname(const QString& nickname) const;
    void createGeneralTab();
    void createConsoleTab();

    QTabWidget* m_tabWidget;

    // General tab
    QComboBox* m_themeCombo;
    QLineEdit* m_nicknameEdit;
    QLabel* m_nicknameStatus;
    QPushButton* m_applyNicknameButton;

    // Console tab
    QTextEdit* m_consoleOutput;
    QLineEdit* m_consoleInput;
    QPushButton* m_sendCommandButton;
    QPushButton* m_clearConsoleButton;
    QCheckBox* m_autoScrollCheckBox;
    QProcess* m_backendProcess;
};

#endif // SETTINGSDIALOG_H