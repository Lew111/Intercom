#include "aboutdialog.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QTabWidget>
#include <QTextBrowser>
#include <QPushButton>
#include <QFrame>
#include <QSysInfo>
#include <QApplication>
#include <QScrollArea>

AboutDialog::AboutDialog(QWidget* parent)
    : QDialog(parent)
{
    setupUI();
    setAttribute(Qt::WA_DeleteOnClose);
    setModal(false);
    if (this->size().isEmpty()) {
        resize(1000, 700);
    }
    setWindowTitle("О программе - InterCom Messenger");
}

AboutDialog::~AboutDialog()
{
}

void AboutDialog::setupUI()
{
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(10);
    mainLayout->setContentsMargins(20, 20, 20, 20);

    QHBoxLayout* titleLayout = new QHBoxLayout();
    QLabel* iconLabel = new QLabel(this);

    QLabel* titleLabel = new QLabel("<h1 style='color: #2c3e50;'>InterCom Messenger</h1>", this);
    titleLabel->setAlignment(Qt::AlignLeft);

    titleLayout->addWidget(iconLabel);
    titleLayout->addWidget(titleLabel);
    titleLayout->addStretch();
    mainLayout->addLayout(titleLayout);

    QHBoxLayout* versionLayout = new QHBoxLayout();
    QLabel* versionLabel = new QLabel("<b>Версия:</b> 1.0.0", this);
    QLabel* buildLabel = new QLabel(QString("<b>Сборка:</b> %1 %2").arg(__DATE__).arg(__TIME__), this);
    versionLayout->addWidget(versionLabel);
    versionLayout->addWidget(buildLabel);
    versionLayout->addStretch();
    mainLayout->addLayout(versionLayout);

    QFrame* line = new QFrame(this);
    line->setFrameShape(QFrame::HLine);
    line->setFrameShadow(QFrame::Sunken);
    mainLayout->addWidget(line);

    m_tabWidget = new QTabWidget(this);
    createInfoTab();
    createChangelogTab();
    mainLayout->addWidget(m_tabWidget);

    QHBoxLayout* buttonLayout = new QHBoxLayout();
    QPushButton* closeButton = new QPushButton("Закрыть", this);
    closeButton->setFixedSize(100, 30);
    buttonLayout->addStretch();
    buttonLayout->addWidget(closeButton);
    mainLayout->addLayout(buttonLayout);

    connect(closeButton, &QPushButton::clicked, this, &QDialog::close);
}

void AboutDialog::createInfoTab()
{
    QWidget* infoTab = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(infoTab);

    QLabel* descLabel = new QLabel(
        "<h2>Курсовой проект:</h2>"
        "<p>Руководитель: Иванов А. Э.</p>"
        "<p>Тема: «Разработка приложения «Чат для общения»»</p>"
        "<h3>О программе</h3>"
        "<p>InterCom Messenger - это современный кроссплатформенный мессенджер "
        "с поддержкой локального бэкенда и автоматической загрузкой сообщений.</p>"

        "<h3>Сведенья об авторе:</h3>"
        "<p>Автор: Пьянзин Вадим Николаевич</p>"
        "<p>Специальность: 09.02.07 «Информационные системы и программирование» ЧОУ «Анапский индустриальный техникум», филиал в г. Темрюке</p>"



        "<h3>Основные возможности</h3>"
        "<ul>"
        "<li><b>Множество чатов</b> - поддержка неограниченного количества диалогов</li>"
        "<li><b>Drag-and-drop (чатов)</b> - удобная сортировка чатов перетаскиванием</li>"
        "<li><b>P2P передача сообщений</b> - Сообщения передаются как напрямую так и пересылка через других пользователей</li>"
        "<li><b>Локальный бэкенд</b> - отсутствие привязки к  конкретному GUI</li>"
        "<li><b>Настраиваемый интерфейс</b> - темы оформления и размер шрифта</li>"
        "<li><b>Кроссплатформенность</b> - работает на Windows и Linux, в планах андройд</li>"
        "</ul>"

        "<h3>Технологии</h3>"
        "<ul>"
        "<li>Qt " QT_VERSION_STR " - фреймворк для графического интерфейса</li>"
        "<li>C++17 - язык программирования</li>"
        "<li>QSettings - сохранение настроек</li>"
        "<li>QListView с кастомной моделью - отображение чатов</li>"
        "</ul>"

        "<h3>Системная информация</h3>"
        "<p>"
        "<b>ОС:</b> " + QSysInfo::prettyProductName() + "<br>"
                                              "<b>Архитектура:</b> " + QSysInfo::currentCpuArchitecture() + "<br>"
                                                   "<b>Qt версия:</b> " + QT_VERSION_STR + "<br>"
                               "<b>Сборка:</b> " + QSysInfo::buildAbi() +
            "</p>",
        infoTab);

    descLabel->setWordWrap(true);
    descLabel->setOpenExternalLinks(true);

    QScrollArea* scrollArea = new QScrollArea(infoTab);
    scrollArea->setWidget(descLabel);
    scrollArea->setWidgetResizable(true);

    layout->addWidget(scrollArea);

    m_tabWidget->addTab(infoTab, "Информация");
}



void AboutDialog::createChangelogTab()
{
    QWidget* changelogTab = new QWidget();
    QVBoxLayout* layout = new QVBoxLayout(changelogTab);

    m_changelogText = new QTextBrowser(changelogTab);
    m_changelogText->setHtml(
        "<h3>Версия 1.0.0 (2026)</h3>"
        "<ul>"
        "<li>Первый публичный релиз</li>"
        "<li>Базовая поддержка чатов</li>"
        "<li>Автоматическая загрузка сообщений</li>"
        "<li>Drag-and-drop сортировка чатов(Порядок чатов)</li>"
        "<li>Настройки интерфейса</li>"
        "</ul>"

        "<h3>Планируемые функции</h3>"
        "<ul>"
        "<li>Отправка файлов и изображений</li>"
        "<li>Поиск по сообщениям</li>"
        "<li>Уведомления на рабочий стол</li>"
        "<li>Шифрование сообщений</li>"
        "<li>Поддержка голосовых сообщений</li>"
        "<li>Эмодзи и реакции</li>"
        "<li>Сворачивание в трей</li>"
        "<li>Горячие клавиши</li>"
        "</ul>"

        "<h3>Известные проблемы</h3>"
        "<ul>"
        "<li>Не работает отправка файлов</li>"
        "</ul>"
        );

    layout->addWidget(m_changelogText);
    m_tabWidget->addTab(changelogTab, "Изменения");
}