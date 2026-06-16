#include "dayseparator.h"
#include "thememanager.h"
#include <QHBoxLayout>
#include <QDate>

DaySeparator::DaySeparator(const QDate& date, QWidget* parent)
    : QWidget(parent)
    , m_date(date)
{
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);

    auto mainLayout = new QHBoxLayout(this);
    mainLayout->setContentsMargins(0, 10, 0, 10);
    mainLayout->setSpacing(10);

    leftLine = new QFrame;
    leftLine->setFrameShape(QFrame::HLine);
    leftLine->setFrameShadow(QFrame::Sunken);
    leftLine->setFixedHeight(1);
    leftLine->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    mainLayout->addWidget(leftLine);

    QString dateStr;
    if (date == QDate::currentDate()) {
        dateStr = "Сегодня";
    } else if (date == QDate::currentDate().addDays(-1)) {
        dateStr = "Вчера";
    } else {
        dateStr = date.toString("dd.MM.yyyy");
    }

    dateLabel = new QLabel(dateStr);
    dateLabel->setAlignment(Qt::AlignCenter);
    mainLayout->addWidget(dateLabel);

    rightLine = new QFrame;
    rightLine->setFrameShape(QFrame::HLine);
    rightLine->setFrameShadow(QFrame::Sunken);
    rightLine->setFixedHeight(1);
    rightLine->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    mainLayout->addWidget(rightLine);

    applyStyle();
}

void DaySeparator::applyStyle()
{
    ThemeManager::Theme theme = ThemeManager::instance().getCurrentTheme();

    QString lineColor = (theme == ThemeManager::Dark) ? "#5a5a5a" : "#cccccc";
    QString textColor = (theme == ThemeManager::Dark) ? "#aaaaaa" : "#666666";

    leftLine->setStyleSheet(QString("background: %1;").arg(lineColor));
    rightLine->setStyleSheet(QString("background: %1;").arg(lineColor));
    dateLabel->setStyleSheet(QString("color: %1; font-size: 11px; padding: 2px 10px;").arg(textColor));
}

void DaySeparator::updateTheme()
{
    applyStyle();
    update();
}