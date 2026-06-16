#include "settingsdialog.h"
#include "config.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPushButton>
#include <QGroupBox>
#include <QSettings>
#include <QFile>
#include <QTextStream>
#include <QDir>
#include <QRegularExpression>
#include <QCoreApplication>
#include <QSplitter>
#include <QScrollBar>
#include <QMenu>
#include <QAction>

SettingsDialog::SettingsDialog(QWidget* parent)
    : QDialog(parent)
    , m_backendProcess(nullptr)
{
    setupUI();
    loadSettings();
    loadNickname();
    setAttribute(Qt::WA_DeleteOnClose);
    setModal(false);
    if (this->size().isEmpty()) {
        resize(600, 500);
    }
    setWindowTitle("Настройки - InterCom Messenger");
}

SettingsDialog::~SettingsDialog()
{
}

void SettingsDialog::setupUI()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    mainLayout->setContentsMargins(10, 10, 10, 10);

    m_tabWidget = new QTabWidget(this);

    createGeneralTab();
    createConsoleTab();

    mainLayout->addWidget(m_tabWidget);

    QHBoxLayout* buttonLayout = new QHBoxLayout();
    QPushButton* closeButton = new QPushButton("Закрыть", this);
    closeButton->setMinimumWidth(100);
    closeButton->setMaximumWidth(150);

    buttonLayout->addStretch();
    buttonLayout->addWidget(closeButton);
    mainLayout->addLayout(buttonLayout);

    connect(closeButton, &QPushButton::clicked, this, &SettingsDialog::saveSettings);
}

void SettingsDialog::createGeneralTab()
{
    QWidget* generalTab = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(generalTab);
    layout->setSpacing(15);
    layout->setContentsMargins(10, 10, 10, 10);

    
    QGroupBox* themeGroup = new QGroupBox("Тема оформления", generalTab);
    QVBoxLayout* themeLayout = new QVBoxLayout(themeGroup);

    QLabel* themeLabel = new QLabel("Выберите тему оформления:", themeGroup);
    m_themeCombo = new QComboBox(themeGroup);
    m_themeCombo->addItem("Светлая", "light");
    m_themeCombo->addItem("Темная", "dark");

    themeLayout->addWidget(themeLabel);
    themeLayout->addWidget(m_themeCombo);
    themeLayout->addStretch();

    layout->addWidget(themeGroup);

    
    QGroupBox* nicknameGroup = new QGroupBox("Настройки ника", generalTab);
    QVBoxLayout* nicknameLayout = new QVBoxLayout(nicknameGroup);

    QLabel* nicknameLabel = new QLabel("Ваш ник:", nicknameGroup);

    QHBoxLayout* nicknameInputLayout = new QHBoxLayout();
    m_nicknameEdit = new QLineEdit(nicknameGroup);
    m_nicknameEdit->setPlaceholderText("Только латиница, цифры и _");
    m_nicknameEdit->setMaxLength(32);

    m_applyNicknameButton = new QPushButton("Применить", nicknameGroup);
    m_applyNicknameButton->setFixedWidth(100);
    m_applyNicknameButton->setEnabled(false);

    m_nicknameStatus = new QLabel(nicknameGroup);
    m_nicknameStatus->setFixedWidth(20);

    nicknameInputLayout->addWidget(m_nicknameEdit);
    nicknameInputLayout->addWidget(m_applyNicknameButton);
    nicknameInputLayout->addWidget(m_nicknameStatus);

    nicknameLayout->addWidget(nicknameLabel);
    nicknameLayout->addLayout(nicknameInputLayout);

    layout->addWidget(nicknameGroup);
    layout->addStretch();

    m_tabWidget->addTab(generalTab, "Основные");

    
    connect(m_themeCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &SettingsDialog::onThemeChanged);
    connect(m_applyNicknameButton, &QPushButton::clicked, this, &SettingsDialog::applyNickname);
    connect(m_nicknameEdit, &QLineEdit::textChanged, this, &SettingsDialog::onNicknameTextChanged);
}

void SettingsDialog::createConsoleTab()
{
    QWidget* consoleTab = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(consoleTab);
    layout->setSpacing(5);
    layout->setContentsMargins(5, 5, 5, 5);

    QLabel* titleLabel = new QLabel("Консоль бекенда", consoleTab);
    titleLabel->setStyleSheet("font-weight: bold; font-size: 12px; padding: 5px;");
    layout->addWidget(titleLabel);

    m_consoleOutput = new QTextEdit(consoleTab);
    m_consoleOutput->setReadOnly(true);
    m_consoleOutput->setFont(QFont("Consolas", 9));
    m_consoleOutput->setStyleSheet("background-color: #1e1e1e; color: #d4d4d4;");
    m_consoleOutput->setLineWrapMode(QTextEdit::WidgetWidth);
    m_consoleOutput->setWordWrapMode(QTextOption::WrapAtWordBoundaryOrAnywhere);

    m_consoleOutput->setContextMenuPolicy(Qt::CustomContextMenu);
    connect(m_consoleOutput, &QTextEdit::customContextMenuRequested,
            this, [this](const QPoint& pos) {
                QMenu* menu = m_consoleOutput->createStandardContextMenu();

                QAction* wrapAction = new QAction("Переносить строки", menu);
                wrapAction->setCheckable(true);
                wrapAction->setChecked(m_consoleOutput->lineWrapMode() == QTextEdit::WidgetWidth);

                connect(wrapAction, &QAction::triggered, this, [this](bool checked) {
                    if (checked) {
                        m_consoleOutput->setLineWrapMode(QTextEdit::WidgetWidth);
                        m_consoleOutput->setWordWrapMode(QTextOption::WrapAtWordBoundaryOrAnywhere);
                    } else {
                        m_consoleOutput->setLineWrapMode(QTextEdit::NoWrap);
                    }
                });

                menu->addSeparator();
                menu->addAction(wrapAction);
                menu->exec(m_consoleOutput->mapToGlobal(pos));

                delete menu;
            });

    layout->addWidget(m_consoleOutput, 1);

    QHBoxLayout* controlLayout = new QHBoxLayout();

    m_clearConsoleButton = new QPushButton("Очистить консоль", consoleTab);
    m_clearConsoleButton->setMaximumWidth(120);

    m_autoScrollCheckBox = new QCheckBox("Автопрокрутка", consoleTab);
    m_autoScrollCheckBox->setChecked(true);  
    m_autoScrollCheckBox->setToolTip("Автоматически прокручивать консоль вниз при новом выводе");

    controlLayout->addWidget(m_clearConsoleButton);
    controlLayout->addWidget(m_autoScrollCheckBox);
    controlLayout->addStretch();

    layout->addLayout(controlLayout);

    QHBoxLayout* inputLayout = new QHBoxLayout();
    inputLayout->setSpacing(5);

    QLabel* inputLabel = new QLabel("Команда:", consoleTab);
    inputLabel->setFixedWidth(60);

    m_consoleInput = new QLineEdit(consoleTab);
    m_consoleInput->setPlaceholderText("Введите команду для бекенда...");
    m_consoleInput->setFont(QFont("Consolas", 9));

    m_sendCommandButton = new QPushButton("Отправить", consoleTab);
    m_sendCommandButton->setFixedWidth(100);

    inputLayout->addWidget(inputLabel);
    inputLayout->addWidget(m_consoleInput);
    inputLayout->addWidget(m_sendCommandButton);

    layout->addLayout(inputLayout);

    QLabel* infoLabel = new QLabel("💡 Совет: Нажмите Enter для отправки команды", consoleTab);
    infoLabel->setStyleSheet("color: #888; font-size: 10px; padding: 5px;");
    layout->addWidget(infoLabel);

    m_tabWidget->addTab(consoleTab, "Консоль");

    connect(m_sendCommandButton, &QPushButton::clicked, this, &SettingsDialog::sendConsoleCommand);
    connect(m_clearConsoleButton, &QPushButton::clicked, this, &SettingsDialog::clearConsole);
    connect(m_consoleInput, &QLineEdit::returnPressed, this, &SettingsDialog::sendConsoleCommand);

    m_consoleOutput->append("<span style='color: #569cd6;'>=== Консоль бекенда ===</span>");
    m_consoleOutput->append("<span style='color: #6a9955;'>Ожидание вывода бекенда...</span>");
}
void SettingsDialog::appendConsoleOutput(const QString& text)
{
    if (!m_consoleOutput) return;

    QStringList lines = text.split('\n');

    for (const QString& line : lines) {
        if (line.isEmpty()) {
            m_consoleOutput->append("");
            continue;
        }

        QString escapedText = line.toHtmlEscaped();

        QString coloredText;
        if (line.contains("error", Qt::CaseInsensitive) ||
            line.contains("failed", Qt::CaseInsensitive) ||
            line.contains("ошибка", Qt::CaseInsensitive)) {
            coloredText = "<span style='color: #f48771;'>" + escapedText + "</span>";
        } else if (line.contains("warning", Qt::CaseInsensitive) ||
                   line.contains("предупреждение", Qt::CaseInsensitive)) {
            coloredText = "<span style='color: #dcdcaa;'>" + escapedText + "</span>";
        } else if (line.contains("success", Qt::CaseInsensitive) ||
                   line.contains("успешно", Qt::CaseInsensitive) ||
                   line.contains("ok", Qt::CaseInsensitive)) {
            coloredText = "<span style='color: #6a9955;'>" + escapedText + "</span>";
        } else if (line.startsWith(">") || line.startsWith("$")) {
            coloredText = "<span style='color: #569cd6; font-weight: bold;'>" + escapedText + "</span>";
        } else if (line.startsWith("[") && line.contains("]")) {
            coloredText = "<span style='color: #9cdcfe;'>" + escapedText + "</span>";
        } else {
            coloredText = "<span style='color: #d4d4d4;'>" + escapedText + "</span>";
        }

        m_consoleOutput->append(coloredText);
    }

    if (m_autoScrollCheckBox && m_autoScrollCheckBox->isChecked()) {
        QScrollBar* scrollBar = m_consoleOutput->verticalScrollBar();
        if (scrollBar) {
            scrollBar->setValue(scrollBar->maximum());
        }
    }
}
void SettingsDialog::sendConsoleCommand()
{
    QString command = m_consoleInput->text().trimmed();
    if (command.isEmpty()) return;

    m_consoleOutput->append("<span style='color: #569cd6;'>> " + command.toHtmlEscaped() + "</span>");

    emit consoleCommandEntered(command);

    m_consoleInput->clear();

    if (m_backendProcess && m_backendProcess->state() == QProcess::Running) {
        QString fullCommand = command + "\n";
        m_backendProcess->write(fullCommand.toUtf8());
        m_backendProcess->waitForBytesWritten(100);
    }
}

void SettingsDialog::clearConsole()
{
    if (m_consoleOutput) {
        m_consoleOutput->clear();
        m_consoleOutput->append("<span style='color: #569cd6;'>=== Консоль очищена ===</span>");
    }
}

void SettingsDialog::setBackendProcess(QProcess* process)
{
    m_backendProcess = process;
}

bool SettingsDialog::isValidNickname(const QString& nickname) const
{
    if (nickname.isEmpty()) {
        return false;
    }

    QRegularExpression re("^[a-zA-Z0-9_]+$");
    return re.match(nickname).hasMatch();
}

void SettingsDialog::onNicknameTextChanged(const QString& text)
{
    if (text.isEmpty()) {
        m_nicknameStatus->setText("❌");
        m_nicknameStatus->setToolTip("Ник не может быть пустым");
        m_applyNicknameButton->setEnabled(false);
    } else if (!isValidNickname(text)) {
        m_nicknameStatus->setText("❌");
        m_nicknameStatus->setToolTip("Только латиница, цифры и _");
        m_applyNicknameButton->setEnabled(false);
    } else {
        m_nicknameStatus->setText("✓");
        m_nicknameStatus->setToolTip("Ник валиден");
        m_applyNicknameButton->setEnabled(true);
    }
}

void SettingsDialog::loadNickname()
{
    QString nicknamePath = QCoreApplication::applicationDirPath() + "/other_data/user_data/nickname.txt";
    QFile file(nicknamePath);

    if (file.exists() && file.open(QIODevice::ReadOnly | QIODevice::Text)) {
        QTextStream in(&file);
        QString nickname = in.readLine().trimmed();
        file.close();

        m_nicknameEdit->setText(nickname);
        qDebug() << "Loaded nickname from file:" << nickname;
    }
}

void SettingsDialog::applyNickname()
{
    QString nickname = m_nicknameEdit->text().trimmed();

    if (!isValidNickname(nickname)) {
        return;
    }

    QString nicknameDir = QCoreApplication::applicationDirPath() + "/other_data/user_data";
    QString nicknamePath = nicknameDir + "/nickname.txt";

    QDir dir;
    if (!dir.exists(nicknameDir)) {
        dir.mkpath(nicknameDir);
    }

    QFile file(nicknamePath);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text | QIODevice::Truncate)) {
        return;
    }

    QTextStream out(&file);
    out << nickname;
    file.close();

    m_nicknameStatus->setText("✓");
    m_nicknameStatus->setToolTip("Ник сохранён");
    m_applyNicknameButton->setEnabled(false);
}

void SettingsDialog::onThemeChanged()
{
    QString theme = m_themeCombo->currentData().toString();
    emit themeChanged(theme);
}

void SettingsDialog::saveSettings()
{
    QSettings settings;
    QString theme = m_themeCombo->currentData().toString();
    settings.setValue("theme", theme);
    settings.sync();

    emit themeChanged(theme);
    close();
}

void SettingsDialog::loadSettings()
{
    QSettings settings;
    QString theme = settings.value("theme", "light").toString();

    int themeIndex = m_themeCombo->findData(theme);
    if (themeIndex >= 0) {
        m_themeCombo->setCurrentIndex(themeIndex);
    }
}