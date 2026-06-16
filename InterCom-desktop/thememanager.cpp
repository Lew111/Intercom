#include "thememanager.h"
#include <QApplication>
#include <QPalette>
#include <QStyle>
#include <QStyleFactory>
#include <QDebug>

ThemeManager& ThemeManager::instance()
{
    static ThemeManager manager;
    return manager;
}

void ThemeManager::applyTheme(Theme theme, QWidget* rootWidget)
{
    m_currentTheme = theme;

    QPalette palette;
    QString styleSheet;

    switch (theme) {
    case Light:
        palette = createLightPalette();
        styleSheet = getGlobalStyleSheet(Light);
        qDebug() << "Applying Light theme";
        break;
    case Dark:
        palette = createDarkPalette();
        styleSheet = getGlobalStyleSheet(Dark);
        qDebug() << "Applying Dark theme";
        break;
    case System:
        palette = createSystemPalette();
        styleSheet = getGlobalStyleSheet(System);
        qDebug() << "Applying System theme";
        break;
    }

    qApp->setPalette(palette);
    qApp->setStyleSheet(styleSheet);

    if (rootWidget) {
        applyPalette(rootWidget, palette);
        rootWidget->setStyleSheet(styleSheet);
    }
}

void ThemeManager::applyTheme(const QString& themeName, QWidget* rootWidget)
{
    if (themeName == "light") {
        applyTheme(Light, rootWidget);
    } else if (themeName == "dark") {
        applyTheme(Dark, rootWidget);
    } else {
        applyTheme(System, rootWidget);
    }
}

QPalette ThemeManager::createLightPalette()
{
    QPalette palette;
    palette.setColor(QPalette::Window, QColor(255, 255, 255));
    palette.setColor(QPalette::WindowText, QColor(0, 0, 0));
    palette.setColor(QPalette::Base, QColor(255, 255, 255));
    palette.setColor(QPalette::AlternateBase, QColor(245, 245, 245));
    palette.setColor(QPalette::Text, QColor(0, 0, 0));
    palette.setColor(QPalette::Button, QColor(240, 240, 240));
    palette.setColor(QPalette::ButtonText, QColor(0, 0, 0));
    palette.setColor(QPalette::Highlight, QColor(0, 120, 215));
    palette.setColor(QPalette::HighlightedText, QColor(255, 255, 255));
    return palette;
}

QPalette ThemeManager::createDarkPalette()
{
    QPalette palette;
    palette.setColor(QPalette::Window, QColor(43, 43, 43));
    palette.setColor(QPalette::WindowText, QColor(255, 255, 255));
    palette.setColor(QPalette::Base, QColor(30, 30, 30));
    palette.setColor(QPalette::AlternateBase, QColor(50, 50, 50));
    palette.setColor(QPalette::Text, QColor(255, 255, 255));
    palette.setColor(QPalette::Button, QColor(60, 60, 60));
    palette.setColor(QPalette::ButtonText, QColor(255, 255, 255));
    palette.setColor(QPalette::Highlight, QColor(0, 120, 215));
    palette.setColor(QPalette::HighlightedText, QColor(255, 255, 255));
    return palette;
}

QPalette ThemeManager::createSystemPalette()
{
    return QApplication::style()->standardPalette();
}

void ThemeManager::applyPalette(QWidget* widget, const QPalette& palette)
{
    if (!widget) return;

    widget->setPalette(palette);

    for (QObject* child : widget->children()) {
        if (QWidget* childWidget = qobject_cast<QWidget*>(child)) {
            applyPalette(childWidget, palette);
        }
    }
}

QString ThemeManager::getGlobalStyleSheet(Theme theme)
{
    if (theme == Light) {
        return R"(
            QScrollBar:vertical {
                background: #f0f0f0;
                width: 10px;
                border-radius: 5px;
            }
            QScrollBar::handle:vertical {
                background: #c0c0c0;
                border-radius: 5px;
                min-height: 20px;
            }
            QScrollBar::handle:vertical:hover {
                background: #a0a0a0;
            }
        )";
    } else {
        return R"(
            QScrollBar:vertical {
                background: #2b2b2b;
                width: 10px;
                border-radius: 5px;
            }
            QScrollBar::handle:vertical {
                background: #5a5a5a;
                border-radius: 5px;
                min-height: 20px;
            }
            QScrollBar::handle:vertical:hover {
                background: #7a7a7a;
            }
            QMenuBar {
                background-color: #3c3c3c;
                color: white;
            }
            QMenuBar::item:selected {
                background-color: #4a4a4a;
            }
            QMenu {
                background-color: #3c3c3c;
                color: white;
                border: 1px solid #5a5a5a;
            }
            QMenu::item:selected {
                background-color: #4a4a4a;
            }
        )";
    }
}

QString ThemeManager::getChatListStyleSheet(Theme theme)
{
    if (theme == Light) {
        return R"(
            QListView {
                background-color: white;
                border: none;
                outline: none;
            }
            QListView::item:selected {
                background-color: transparent;
            }
            QListView::item:hover {
                background-color: #f5f5f5;
            }
        )";
    } else {
        return R"(
            QListView {
                background-color: #2b2b2b;
                border: none;
                outline: none;
            }
            QListView::item {
                color: white;
            }
            QListView::item:selected {
                background-color: transparent;
            }
            QListView::item:hover {
                background-color: #3a3a3a;
            }
        )";
    }
}

QString ThemeManager::getMessageWidgetStyleSheet(bool isMine, Theme theme, const QString& style)
{
    Q_UNUSED(style);

    if (theme == Light) {
        if (isMine) {
            return "background:#4e9cff; color:white; border-radius:10px; padding:1px;";
        } else {
            return "background:#e5e5e5; color:black; border-radius:10px; padding:1px;";
        }
    } else {
        if (isMine) {
            return "background:#3a7bc8; color:white; border-radius:10px; padding:1px;";
        } else {
            return "background:#3a3a3a; color:white; border-radius:10px; padding:1px;";
        }
    }
}

QString ThemeManager::getDaySeparatorStyleSheet(Theme theme)
{
    if (theme == Light) {
        return "color: #666666; font-size: 11px; padding: 2px 10px;";
    } else {
        return "color: #aaaaaa; font-size: 11px; padding: 2px 10px;";
    }
}

QString ThemeManager::getInputStyleSheet(Theme theme)
{
    if (theme == Light) {
        return R"(
            QTextEdit {
                border: 1px solid #ccc;
                border-radius: 5px;
                padding: 5px;
                background: white;
                color: black;
            }
            QTextEdit:focus {
                border: 1px solid #4e9cff;
            }
        )";
    } else {
        return R"(
            QTextEdit {
                border: 1px solid #5a5a5a;
                border-radius: 5px;
                padding: 5px;
                background: #3a3a3a;
                color: white;
            }
            QTextEdit:focus {
                border: 1px solid #4e9cff;
            }
        )";
    }
}