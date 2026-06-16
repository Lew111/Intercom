#pragma once

#ifndef DAYSEPARATOR_H
#define DAYSEPARATOR_H

#include <QWidget>
#include <QLabel>
#include <QFrame>
#include <QDate>
#include "thememanager.h"

class DaySeparator : public QWidget
{
    Q_OBJECT
public:
    explicit DaySeparator(const QDate& date, QWidget* parent = nullptr);

    void updateTheme();  

private:
    void applyStyle();

    QLabel* dateLabel;
    QFrame* leftLine;
    QFrame* rightLine;
    QDate m_date;
};

#endif 